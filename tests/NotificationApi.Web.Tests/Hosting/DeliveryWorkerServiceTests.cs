using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotificationApi.Domain.Notifications;
using NotificationApi.Web.Hosting;
using NotificationApi.Web.Tests.Acceptance;

namespace NotificationApi.Web.Tests.Hosting;

/// <summary>The delivery worker's shutdown behaviour (ADR 0014).</summary>
public sealed class DeliveryWorkerServiceTests
{
    [Fact]
    public async Task Stopping_waits_for_sends_in_flight_so_a_graceful_stop_leaves_nothing_sending()
    {
        await using var app = await DeliveryApp.StartAsync();
        await app.SeedFixturesAsync();
        app.Telegram.Hold = true;
        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "contact:bob" }, message = "m", priority = "normal" });
        await app.WaitForDeliveriesAsync(accepted.NotificationId, d => d.Status == DeliveryStatus.Sending, "the send is in flight");
        var worker = Worker(app);

        var stopping = worker.StopAsync(TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        stopping.IsCompleted.ShouldBeFalse("the worker waits for the send");
        app.Telegram.ReleaseAll();
        await stopping;

        (await app.DeliveriesAsync(accepted.NotificationId)).Single().Status.ShouldBe(DeliveryStatus.Sent);
    }

    [Fact]
    public async Task When_the_host_stops_waiting_the_send_is_cut_off_and_stays_sending_for_the_restart_rule()
    {
        await using var app = await DeliveryApp.StartAsync();
        await app.SeedFixturesAsync();
        app.Telegram.Hold = true;
        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "contact:bob" }, message = "m", priority = "normal" });
        await app.WaitForDeliveriesAsync(accepted.NotificationId, d => d.Status == DeliveryStatus.Sending, "the send is in flight");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Worker(app).StopAsync(timeout.Token);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        (await app.DeliveriesAsync(accepted.NotificationId)).Single().Status.ShouldBe(DeliveryStatus.Sending);
    }

    private static DeliveryWorkerService Worker(DeliveryApp app) =>
        app.Factory.Services.GetServices<IHostedService>().OfType<DeliveryWorkerService>().Single();
}
