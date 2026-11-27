using NotificationApi.Application.Common;

namespace NotificationApi.Application.Discovery;

/// <summary>Publishes a Supervisor discovery message. Implemented in Infrastructure against <c>http://supervisor/discovery</c>.</summary>
public interface IDiscoveryPublisher
{
    /// <summary>False outside HA OS (no <c>SUPERVISOR_TOKEN</c>): local development and tests.</summary>
    bool IsConfigured { get; }

    /// <summary>Makes exactly one attempt. Retries belong to <see cref="PublishDiscovery"/>.</summary>
    Task<Result<DiscoveryReceipt>> PublishAsync(DiscoveryMessage message, CancellationToken cancellationToken);
}
