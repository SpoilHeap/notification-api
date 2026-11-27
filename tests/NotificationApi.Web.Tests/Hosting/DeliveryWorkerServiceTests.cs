using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotificationApi.Application.Common;
using NotificationApi.Application.Events;
using NotificationApi.Application.Notifications;
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

    [Fact]
    public async Task A_crashed_attempt_is_marked_failed_at_once_with_an_event_and_not_sent_again()
    {
        var saves = new FailingSaves();
        var factory = new NotificationApiFactory();
        factory.TestServices.Add(services =>
        {
            var inner = services.Single(d => d.ServiceType == typeof(IUnitOfWork));
            services.Remove(inner);
            services.AddScoped<IUnitOfWork>(sp => new FailingUnitOfWork((IUnitOfWork)ActivatorUtilities.CreateInstance(sp, inner.ImplementationType!), saves));
        });
        await using var app = await DeliveryApp.StartAsync(factory: factory);
        await app.SeedFixturesAsync();
        await app.RegisterWebhookAsync();
        saves.FailWhen = () => app.Telegram.Calls > 0; // saving the outcome after the send fails once

        var accepted = await app.NotifyAcceptedAsync(new { to = new[] { "contact:bob" }, message = "m", priority = "normal" });

        var delivery = (await app.WaitForFinalAsync(accepted.NotificationId)).Single();
        delivery.Status.ShouldBe(DeliveryStatus.Failed);
        delivery.LastError.ShouldBe(AbandonDelivery.AbandonedError);
        var events = await app.WaitForHaEventsAsync(1);
        events.Single().GetProperty("type").GetString().ShouldBe(HaEventTypes.DeliveryFailed);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        app.Telegram.Calls.ShouldBe(1, "never sent again");
    }

    private sealed class FailingSaves
    {
        private int _failed;

        public Func<bool> FailWhen { get; set; } = () => false;

        public bool ShouldFail() => FailWhen() && Interlocked.Exchange(ref _failed, 1) == 0;
    }

    private sealed class FailingUnitOfWork(IUnitOfWork inner, FailingSaves saves) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            saves.ShouldFail() ? throw new InvalidOperationException("The database is locked.") : inner.SaveChangesAsync(cancellationToken);
    }

    private static DeliveryWorkerService Worker(DeliveryApp app) =>
        app.Factory.Services.GetServices<IHostedService>().OfType<DeliveryWorkerService>().Single();
}
