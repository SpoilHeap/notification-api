using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.Events;
using NotificationApi.Infrastructure.Events;

namespace NotificationApi.Infrastructure.Tests.Events;

public sealed class ChannelPendingHaEventsTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 16, 15, 20, TimeSpan.Zero));

    [Fact]
    public async Task Events_are_read_in_the_order_they_were_queued()
    {
        var queue = new ChannelPendingHaEvents(new CapturingLogger());
        var events = Enumerable.Range(2, 3).Select(i => HaEvent.RecipientsChanged(i.ToString(System.Globalization.CultureInfo.InvariantCulture), _time)).ToList();

        events.ForEach(queue.Enqueue);
        var read = await ReadAsync(queue, events.Count);

        read.ShouldBe(events);
    }

    [Fact]
    public async Task When_full_the_oldest_event_is_dropped_and_logged_without_blocking()
    {
        var logger = new CapturingLogger();
        var queue = new ChannelPendingHaEvents(logger);
        var events = Enumerable.Range(0, ChannelPendingHaEvents.Capacity + 2).Select(_ => HaEvent.WebhookTest(_time)).ToList();

        events.ForEach(queue.Enqueue); // never waits

        var read = await ReadAsync(queue, ChannelPendingHaEvents.Capacity);
        read[0].ShouldBe(events[2]);
        read[^1].ShouldBe(events[^1]);
        logger.Warnings.Count.ShouldBe(2);
        logger.Warnings[0].ShouldContain(events[0].Id);
    }

    [Fact]
    public void Enqueue_requires_an_event() =>
        Should.Throw<ArgumentNullException>(() => new ChannelPendingHaEvents(new CapturingLogger()).Enqueue(null!));

    private static async Task<List<HaEvent>> ReadAsync(ChannelPendingHaEvents queue, int count)
    {
        var read = new List<HaEvent>();
        await foreach (var haEvent in queue.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            read.Add(haEvent);
            if (read.Count == count)
            {
                break;
            }
        }

        return read;
    }

    private sealed class CapturingLogger : ILogger<ChannelPendingHaEvents>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
