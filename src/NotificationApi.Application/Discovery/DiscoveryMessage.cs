using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.Discovery;

/// <summary>
/// <c>{"service": "notification_api", "config": {"host": ..., "port": ..., "token": ...}}</c> (spec §9.4).
/// The HA integration reads <see cref="DiscoveryConfig"/> in <c>async_step_hassio</c>.
/// </summary>
public sealed record DiscoveryMessage(string Service, DiscoveryConfig Config);

/// <param name="Host">The add-on's hostname on the Supervisor network.</param>
/// <param name="Port">The API port.</param>
/// <param name="Token">The plaintext API token. Its <c>ToString</c> is redacted, so logging this record is safe.</param>
public sealed record DiscoveryConfig(string Host, int Port, ApiToken Token);

/// <summary>The Supervisor's acknowledgement: the discovery entry's id.</summary>
public sealed record DiscoveryReceipt(string Uuid);
