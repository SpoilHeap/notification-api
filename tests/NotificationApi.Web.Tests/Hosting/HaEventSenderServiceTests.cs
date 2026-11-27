using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NotificationApi.Application.Events;

namespace NotificationApi.Web.Tests.Hosting;

/// <summary>The background sender behind <see cref="IPendingHaEvents"/>.</summary>
public sealed class HaEventSenderServiceTests
{
    [Fact]
    public async Task An_event_that_fails_unexpectedly_is_logged_and_later_events_are_still_sent()
    {
        var publisher = new ThrowOncePublisher();
        await using var factory = new NotificationApiFactory();
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IHaEventPublisher>(publisher)));
        using var client = app.CreateClient();
        (await client.PutWebhookAsync(await app.Services.GetApiTokenAsync(), "http://homeassistant:8123/api/webhook/abc", TestRequests.NewWebhookSecret())).Dispose();
        var time = TimeProvider.System;
        var pending = app.Services.GetRequiredService<IPendingHaEvents>();
        var first = HaEvent.RecipientsChanged("2", time);
        var second = HaEvent.RecipientsChanged("3", time);

        pending.Enqueue(first);
        pending.Enqueue(second);
        await TestRequests.WaitUntilAsync(() => publisher.Sent.Contains(second.Id), "the second event is still sent");

        factory.Logs.Entries.ShouldContain(e => e.StartsWith("Warning", StringComparison.Ordinal) && e.Contains(first.Id, StringComparison.Ordinal) && e.Contains("unexpected failure", StringComparison.Ordinal));
    }

    private sealed class ThrowOncePublisher : IHaEventPublisher
    {
        private int _calls;

        public System.Collections.Concurrent.ConcurrentBag<string> Sent { get; } = [];

        public Task<WebhookSendResult> PublishAsync(WebhookTarget target, HaEvent haEvent, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                throw new InvalidOperationException("boom");
            }

            Sent.Add(haEvent.Id);
            return Task.FromResult(WebhookSendResult.Delivered(200));
        }
    }
}
