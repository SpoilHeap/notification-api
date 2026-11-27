using System.Text.Json;
using NotificationApi.Application.Channels;
using NotificationApi.Application.Events;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Web.Tests.Acceptance;

/// <summary>Spec §12.3: delivery events to the HA webhook (AC30, AC31, AC34).</summary>
public sealed class DeliveryEventsAcceptanceTests
{
    [Fact]
    public async Task AC30_Each_sent_delivery_posts_one_signed_delivery_sent_event_with_the_spec_fields()
    {
        await using var app = await DeliveryApp.StartAsync();
        await app.SeedFixturesAsync();
        await app.RegisterWebhookAsync();
        await app.GroupAsync("family", "contact:alice");

        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "group:family" }, message = "m", priority = "high", tag = "front_door_alarm", source = "automation.front_door" });
        await app.WaitForFinalAsync(accepted.NotificationId);

        var events = await app.WaitForHaEventsAsync(2);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        app.Ha!.LogEntries.Count.ShouldBe(2, "one event per delivery");
        events.ShouldAllBe(e => e.GetProperty("type").GetString() == HaEventTypes.DeliverySent);
        var data = events.Select(e => e.GetProperty("data")).OrderBy(d => d.GetProperty("channel").GetString()).ToList();
        data[0].EnumerateObject().Select(p => p.Name).ShouldBe(["notification_id", "delivery_id", "contact", "via", "channel", "tag", "source"]);
        data.Select(d => d.ToString()).ShouldBe(
        [
            Expected(accepted, 1, "sms"),
            Expected(accepted, 0, "telegram"),
        ]);
        app.Ha.LogEntries.ShouldAllBe(e => e.RequestMessage!.Headers![WebhookSignature.SignatureHeader].Single().StartsWith(WebhookSignature.Prefix));

        static string Expected(DeliveryApp.Accepted accepted, int index, string channel) => JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["notification_id"] = accepted.NotificationId,
            ["delivery_id"] = accepted.Deliveries[index].DeliveryId,
            ["contact"] = "alice",
            ["via"] = "group:family",
            ["channel"] = channel,
            ["tag"] = "front_door_alarm",
            ["source"] = "automation.front_door",
        });
    }

    [Fact]
    public async Task AC30_An_absent_tag_and_source_are_null()
    {
        await using var app = await DeliveryApp.StartAsync();
        await app.SeedFixturesAsync();
        await app.RegisterWebhookAsync();

        await app.NotifyAcceptedAsync(new { to = new[] { "contact:bob" }, message = "m", priority = "normal" });

        var data = (await app.WaitForHaEventsAsync(1)).Single().GetProperty("data");
        data.GetProperty("tag").ValueKind.ShouldBe(JsonValueKind.Null);
        data.GetProperty("source").ValueKind.ShouldBe(JsonValueKind.Null);
        data.GetProperty("contact").GetString().ShouldBe("bob");
        data.GetProperty("via").GetString().ShouldBe("contact:bob");
    }

    [Fact]
    public async Task AC31_A_failed_delivery_posts_one_delivery_failed_event_with_the_error()
    {
        await using var app = await DeliveryApp.StartAsync(factory: DeliveryApp.FastRetries());
        await app.SeedFixturesAsync();
        await app.RegisterWebhookAsync();
        app.Telegram.Answer = (_, n) => ChannelResult.Failed($"Telegram error {n}");

        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "contact:bob" }, message = "m", priority = "normal", tag = "t" });
        await app.WaitForFinalAsync(accepted.NotificationId);

        var haEvent = (await app.WaitForHaEventsAsync(1)).ShouldHaveSingleItem();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        app.Ha!.LogEntries.Count.ShouldBe(1, "no event for the retries, one for the final state");
        haEvent.GetProperty("type").GetString().ShouldBe(HaEventTypes.DeliveryFailed);
        var data = haEvent.GetProperty("data");
        data.EnumerateObject().Select(p => p.Name).ShouldBe(["notification_id", "delivery_id", "contact", "via", "channel", "tag", "source", "error"]);
        data.GetProperty("notification_id").GetString().ShouldBe(accepted.NotificationId);
        data.GetProperty("delivery_id").GetString().ShouldBe(accepted.Deliveries[0].DeliveryId);
        data.GetProperty("channel").GetString().ShouldBe("telegram");
        data.GetProperty("tag").GetString().ShouldBe("t");
        data.GetProperty("error").GetString().ShouldBe("Telegram error 4");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(0)] // unreachable
    public async Task AC34_An_unreachable_webhook_does_not_change_delivery_outcomes_and_is_logged(int status)
    {
        await using var app = await DeliveryApp.StartAsync();
        await app.SeedFixturesAsync();
        if (status == 0)
        {
            using var put = await app.Client.PutWebhookAsync(app.Token, "http://127.0.0.1:1/api/webhook/x", TestRequests.NewWebhookSecret());
        }
        else
        {
            await app.RegisterWebhookAsync(status);
        }

        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, message = "m", priority = "high" });

        (await app.WaitForFinalAsync(accepted.NotificationId)).ShouldAllBe(d => d.Status == DeliveryStatus.Sent);
        await TestRequests.WaitUntilAsync(
            () => app.Factory.Logs.Entries.Count(e => e.StartsWith("Warning", StringComparison.Ordinal) && e.Contains("delivery.sent", StringComparison.Ordinal) && e.Contains("was not delivered", StringComparison.Ordinal)) == 2,
            "each failed event is logged at Warning");
        (await app.DeliveriesAsync(accepted.NotificationId)).ShouldAllBe(d => d.Status == DeliveryStatus.Sent && d.Attempts == 1);
    }
}
