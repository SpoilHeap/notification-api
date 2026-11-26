using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Channels;

/// <summary>
/// Port for a delivery channel (Telegram, SMS.to, …). Implementations live in Infrastructure.
/// A channel makes exactly one send attempt. Retries belong to the delivery algorithm (spec §5).
/// </summary>
public interface INotificationChannel
{
    ChannelType Type { get; }

    Task<ChannelResult> SendAsync(OutboundMessage message, CancellationToken cancellationToken);
}
