using System.Threading.Channels;

namespace NotificationApi.Application.Notifications;

/// <summary>
/// Wakes the delivery worker as soon as there is new work (a notification was queued, or a retry was scheduled),
/// so it doesn't depend on polling (ADR 0014). A singleton. Repeated signals before the worker wakes collapse into one.
/// </summary>
public sealed class DeliveryWakeUp
{
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>Never blocks.</summary>
    public void Notify() => _signal.Writer.TryWrite(true);

    /// <summary>
    /// Returns when <see cref="Notify"/> is called (or was called since the last wait), or after
    /// <paramref name="timeout"/> on <paramref name="time"/>'s clock, whichever is first.
    /// </summary>
    public async Task WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(time);
        if (_signal.Reader.TryRead(out _))
        {
            return;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalled = _signal.Reader.WaitToReadAsync(stop.Token).AsTask();
        var delay = Task.Delay(timeout, time, stop.Token);
        await Task.WhenAny(signalled, delay);
        await stop.CancelAsync();
        _signal.Reader.TryRead(out _);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
