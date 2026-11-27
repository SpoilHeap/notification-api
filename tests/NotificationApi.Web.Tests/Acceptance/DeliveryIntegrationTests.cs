using System.Diagnostics;
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NotificationApi.Application.Common;
using NotificationApi.Application.Notifications;
using NotificationApi.Domain.Notifications;
using NotificationApi.Web.Tests.Components;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NotificationApi.Web.Tests.Acceptance;

/// <summary>Integration-level criteria (spec §12 "I"): AC13, AC22, AC23.</summary>
public sealed class DeliveryIntegrationTests
{
    [Fact]
    public async Task AC13_Notify_answers_within_500_ms_p95_whatever_the_provider_latency()
    {
        await using var app = await DeliveryApp.StartAsync();
        await app.SeedFixturesAsync();
        app.Telegram.Delay = TimeSpan.FromSeconds(3);
        app.Sms.Delay = TimeSpan.FromSeconds(3);
        var body = new { to = new[] { "contact:alice", "contact:bob" }, message = "m", priority = "high" };
        await app.NotifyAcceptedAsync(body); // warm-up

        var timings = new List<TimeSpan>();
        for (var i = 0; i < 20; i++)
        {
            var watch = Stopwatch.StartNew();
            using var response = await app.NotifyAsync(body);
            watch.Stop();
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            timings.Add(watch.Elapsed);
        }

        var p95 = timings.Order().ElementAt((int)Math.Ceiling(timings.Count * 0.95) - 1);
        p95.ShouldBeLessThan(TimeSpan.FromMilliseconds(500), $"timings: {string.Join(", ", timings.Select(t => (int)t.TotalMilliseconds))} ms");
        app.Telegram.Calls.ShouldBeGreaterThan(0, "the slow sends ran in the background");
    }

    [Fact]
    public async Task AC22_Sms_to_accepting_a_message_makes_the_delivery_sent_and_stores_its_message_id()
    {
        using var smsTo = WireMockServer.Start();
        smsTo.Given(Request.Create().WithPath("/sms/send").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"message":"Message is queued for sending! Please check report for update","success":true,"message_id":"11ec-832f-a6f3fcfe-9fea-02420a0002ab"}"""));
        var factory = new NotificationApiFactory();
        factory.Settings["SmsTo:BaseUrl"] = smsTo.Url!;
        await using var app = await DeliveryApp.StartAsync(channels: false, factory: factory);
        await app.SeedFixturesAsync();
        using (var ui = UiClient.For(app.Factory))
        {
            await ui.SubmitAsync("/settings", "save-smsto", ("SmsTo.ApiKey", "live-key-abcdefghijkl"), ("SmsTo.SenderId", "Home"));
        }

        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, title = "Door", message = "Opened", priority = "normal", channels = new[] { "sms" } });

        var delivery = (await app.WaitForFinalAsync(accepted.NotificationId)).ShouldHaveSingleItem();
        (delivery.Status, delivery.Attempts, delivery.ProviderMessageId).ShouldBe((DeliveryStatus.Sent, 1, "11ec-832f-a6f3fcfe-9fea-02420a0002ab"));
        var request = smsTo.LogEntries.ShouldHaveSingleItem().RequestMessage!;
        request.Headers!["Authorization"].ShouldBe(["Bearer live-key-abcdefghijkl"]);
        request.Body.ShouldBe("""{"to":"+353861234567","message":"Door: Opened","sender_id":"Home"}""");
    }

    [Fact]
    public async Task An_sms_with_sms_to_not_configured_fails_with_a_clear_error()
    {
        var factory = DeliveryApp.FastRetries(maxRetries: 0);
        factory.Settings["SmsTo:BaseUrl"] = "http://127.0.0.1:1/";
        await using var app = await DeliveryApp.StartAsync(channels: false, factory: factory);
        await app.SeedFixturesAsync();

        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, message = "m", priority = "normal", channels = new[] { "sms" } });

        var delivery = (await app.WaitForFinalAsync(accepted.NotificationId)).ShouldHaveSingleItem();
        (delivery.Status, delivery.LastError).ShouldBe((DeliveryStatus.Failed, "SMS.to is not configured"));
    }

    [Fact]
    public async Task AC23_After_a_restart_queued_and_retrying_deliveries_are_sent_once_and_an_interrupted_send_is_not_repeated()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "notification-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string queued, retrying, interrupted;
            await using (var before = await DeliveryApp.StartAsync(worker: false, factory: new NotificationApiFactory { DataPath = dataPath, DeleteDataOnDispose = false }))
            {
                await before.SeedFixturesAsync();
                queued = (await before.NotifyAcceptedAsync(new { to = new[] { "contact:bob" }, message = "queued", priority = "normal" })).NotificationId;
                retrying = (await before.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, message = "retrying", priority = "normal" })).NotificationId;
                interrupted = (await before.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, message = "interrupted", priority = "normal", channels = new[] { "sms" } })).NotificationId;

                // As the worker would have left them: one attempt failed (retry due shortly), one cut off mid-send.
                await before.InScopeAsync(async sp =>
                {
                    var repository = sp.GetRequiredService<INotificationRepository>();
                    var now = DateTimeOffset.UtcNow;
                    var retry = (await repository.ListDeliveriesAsync(Id(retrying), CancellationToken.None)).Single();
                    retry.StartAttempt(now);
                    retry.RecordFailedAttempt("Telegram timed out", now, new RetryPolicy(3, TimeSpan.FromMilliseconds(200)));
                    (await repository.ListDeliveriesAsync(Id(interrupted), CancellationToken.None)).Single().StartAttempt(now);
                    await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
                });
                before.Telegram.Calls.ShouldBe(0);
            }

            await using var after = await DeliveryApp.StartAsync(factory: new NotificationApiFactory { DataPath = dataPath, DeleteDataOnDispose = false });

            (await after.WaitForFinalAsync(queued)).Single().ShouldSatisfyAllConditions(d => d.Status.ShouldBe(DeliveryStatus.Sent), d => d.Attempts.ShouldBe(1));
            (await after.WaitForFinalAsync(retrying)).Single().ShouldSatisfyAllConditions(d => d.Status.ShouldBe(DeliveryStatus.Sent), d => d.Attempts.ShouldBe(2));
            var cutOff = (await after.WaitForFinalAsync(interrupted)).Single();
            (cutOff.Status, cutOff.Attempts, cutOff.LastError).ShouldBe((DeliveryStatus.Failed, 1, RecoverInterruptedDeliveries.InterruptedError));
            await Task.Delay(300, TestContext.Current.CancellationToken);
            after.Telegram.Sent.Select(m => m.Body).Order().ToList().ShouldBe(["queued", "retrying"], "each sent exactly once");
            after.Sms.Sent.ShouldBeEmpty("the interrupted send is never repeated");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, recursive: true);
            }
        }

        static NotificationId Id(string text) => NotificationId.TryParse(text, out var id) ? id : throw new FormatException(text);
    }
}
