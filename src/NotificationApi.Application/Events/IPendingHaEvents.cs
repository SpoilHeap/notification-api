namespace NotificationApi.Application.Events;

/// <summary>
/// Hands events to a background sender, so a request never waits for HA (up to the 5 s webhook timeout). Bounded:
/// when full, the oldest waiting event is dropped and logged. Events are best-effort anyway (AC34), and the
/// integration also polls <c>GET /recipients</c>. Implemented in Infrastructure; a hosted service drains it through
/// <see cref="PublishHaEvent"/>.
/// </summary>
public interface IPendingHaEvents
{
    /// <summary>Never blocks.</summary>
    void Enqueue(HaEvent haEvent);

    /// <summary>Waits for events, in the order they were enqueued, until cancelled.</summary>
    IAsyncEnumerable<HaEvent> ReadAllAsync(CancellationToken cancellationToken);
}
