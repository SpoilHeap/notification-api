using NotificationApi.Application.Channels;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Web.Tests.Acceptance;

/// <summary>Spec §12.2 through the API: retries, formatting and independence of deliveries (AC20, AC21, AC24–AC26).</summary>
public sealed class DeliveryAcceptanceTests
{
    private static readonly object ToBob = new { to = new[] { "contact:bob" }, message = "m", priority = "normal" };

    [Fact]
    public async Task AC20_A_channel_that_fails_every_attempt_gets_1_plus_3_retries_at_30_second_intervals_and_ends_failed()
    {
        await using var app = await DeliveryApp.StartAsync(fakeTime: true);
        await app.SeedFixturesAsync();
        app.Telegram.Answer = (_, n) => ChannelResult.Failed($"Telegram error {n}");

        var accepted = await app.NotifyAcceptedAsync(ToBob);
        await app.WaitForDeliveriesAsync(accepted.NotificationId, d => d.Status == DeliveryStatus.Retrying && d.Attempts == 1, "attempt 1 failed");

        for (var attempt = 2; attempt <= 4; attempt++)
        {
            app.Time!.Advance(TimeSpan.FromSeconds(29));
            await Task.Delay(150, TestContext.Current.CancellationToken);
            app.Telegram.Calls.ShouldBe(attempt - 1, "no retry before 30 s");

            app.Time.Advance(TimeSpan.FromSeconds(1));
            var expected = attempt;
            await app.WaitForDeliveriesAsync(
                accepted.NotificationId,
                d => d.Attempts == expected && d.Status is DeliveryStatus.Retrying or DeliveryStatus.Failed,
                $"attempt {attempt} recorded");
        }

        var delivery = (await app.DeliveriesAsync(accepted.NotificationId)).ShouldHaveSingleItem();
        (delivery.Status, delivery.Attempts, delivery.LastError).ShouldBe((DeliveryStatus.Failed, 4, "Telegram error 4"));
        var at = app.Telegram.SentAt;
        at.Zip(at.Skip(1), (a, b) => b - a).ShouldBe([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)]);
        app.Time!.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(150, TestContext.Current.CancellationToken);
        app.Telegram.Calls.ShouldBe(4);
    }

    [Fact]
    public async Task AC21_A_channel_that_fails_once_then_succeeds_ends_sent_with_2_attempts()
    {
        await using var app = await DeliveryApp.StartAsync(factory: DeliveryApp.FastRetries());
        await app.SeedFixturesAsync();
        app.Telegram.Answer = (_, n) => n == 1 ? ChannelResult.Failed("flaky") : ChannelResult.Sent("tg-2");

        var accepted = await app.NotifyAcceptedAsync(ToBob);

        var delivery = (await app.WaitForFinalAsync(accepted.NotificationId)).ShouldHaveSingleItem();
        (delivery.Status, delivery.Attempts, delivery.ProviderMessageId).ShouldBe((DeliveryStatus.Sent, 2, "tg-2"));
    }

    [Fact]
    public async Task AC24_AC25_Telegram_gets_escaped_html_and_sms_gets_title_colon_message()
    {
        await using var app = await DeliveryApp.StartAsync();
        await app.SeedFixturesAsync();

        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, title = "Door <b>& co", message = "<script>alert(1)</script>", priority = "high" });
        await app.WaitForFinalAsync(accepted.NotificationId);

        app.Telegram.Sent.ShouldHaveSingleItem().Text.ShouldBe("<b>Door &lt;b&gt;&amp; co</b>\n&lt;script&gt;alert(1)&lt;/script&gt;");
        app.Sms.Sent.ShouldHaveSingleItem().Text.ShouldBe("Door <b>& co: <script>alert(1)</script>");
    }

    [Fact]
    public async Task AC25_With_no_title_sms_and_telegram_get_just_the_message()
    {
        await using var app = await DeliveryApp.StartAsync();
        await app.SeedFixturesAsync();

        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, message = "Just this & that", priority = "high" });
        await app.WaitForFinalAsync(accepted.NotificationId);

        app.Sms.Sent.ShouldHaveSingleItem().Text.ShouldBe("Just this & that");
        app.Telegram.Sent.ShouldHaveSingleItem().Text.ShouldBe("Just this &amp; that");
    }

    [Fact]
    public async Task AC26_A_stuck_delivery_does_not_delay_the_others_of_the_same_notification()
    {
        await using var app = await DeliveryApp.StartAsync();
        await app.SeedFixturesAsync();
        app.Telegram.Hold = true;

        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, message = "m", priority = "high" });

        await app.WaitForDeliveriesAsync(
            accepted.NotificationId,
            d => d.Channel == ChannelType.Sms ? d.Status == DeliveryStatus.Sent : d.Status == DeliveryStatus.Sending,
            "SMS is sent while Telegram is still stuck");
        app.Telegram.ReleaseAll();
        (await app.WaitForFinalAsync(accepted.NotificationId)).ShouldAllBe(d => d.Status == DeliveryStatus.Sent && d.Attempts == 1);
    }

    [Fact]
    public async Task AC26_A_failing_delivery_does_not_affect_the_others_of_the_same_notification()
    {
        await using var app = await DeliveryApp.StartAsync(factory: DeliveryApp.FastRetries());
        await app.SeedFixturesAsync();
        app.Telegram.Answer = (_, _) => ChannelResult.Failed("Telegram is down");
        await app.GroupAsync("family", "contact:alice", "contact:bob");

        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "group:family" }, message = "m", priority = "high" });

        var deliveries = await app.WaitForFinalAsync(accepted.NotificationId);
        deliveries.Select(d => $"{d.Contact.Name}/{d.Channel}/{d.Status}/{d.Attempts}")
            .ShouldBe(["alice/Telegram/Failed/4", "alice/Sms/Sent/1", "bob/Telegram/Failed/4"]);
    }

    [Fact]
    public async Task A_channel_with_no_implementation_fails_at_once_with_a_clear_error()
    {
        await using var app = await DeliveryApp.StartAsync(telegram: false);
        await app.SeedFixturesAsync();

        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, message = "m", priority = "high" });

        var deliveries = await app.WaitForFinalAsync(accepted.NotificationId);
        deliveries[0].Status.ShouldBe(DeliveryStatus.Failed);
        deliveries[0].Attempts.ShouldBe(1);
        deliveries[0].LastError.ShouldBe("The Telegram channel isn't available in this version of the add-on.");
        deliveries[1].Status.ShouldBe(DeliveryStatus.Sent);
    }
}
