using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NotificationApi.Application.Events;

namespace NotificationApi.Infrastructure.Events;

/// <summary>
/// <see cref="IPendingHaEvents"/> over a bounded in-process channel (a singleton). Writers never wait: when
/// <see cref="Capacity"/> events are already waiting, the oldest is dropped and logged. Nothing survives a restart,
/// which is fine for best-effort events (AC34): the integration also polls <c>GET /recipients</c>.
/// </summary>
internal sealed partial class ChannelPendingHaEvents : IPendingHaEvents
{
    public const int Capacity = 256;

    private readonly Channel<HaEvent> _channel;
    private readonly ILogger<ChannelPendingHaEvents> _logger;

    public ChannelPendingHaEvents(ILogger<ChannelPendingHaEvents> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<HaEvent>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            dropped => LogDropped(_logger, dropped.Type, dropped.Id, Capacity));
    }

    public void Enqueue(HaEvent haEvent)
    {
        ArgumentNullException.ThrowIfNull(haEvent);
        _channel.Writer.TryWrite(haEvent); // always succeeds with DropOldest
        LogQueued(_logger, haEvent.Type, haEvent.Id);
    }

    public IAsyncEnumerable<HaEvent> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Queued event {EventType} {EventId}.")]
    private static partial void LogQueued(ILogger logger, string eventType, string eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped event {EventType} {EventId}: {Capacity} events were already waiting to be sent.")]
    private static partial void LogDropped(ILogger logger, string eventType, string eventId, int capacity);
}
