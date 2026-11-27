using Microsoft.Extensions.Options;

namespace NotificationApi.Application.Discovery;

/// <summary>What the Integration page shows: the discovery outcome and the values for manual setup.</summary>
public sealed record IntegrationDetails(DiscoveryStatus Discovery, string Host, int Port);

public sealed class GetIntegrationDetails(DiscoveryStatusStore status, IOptions<DiscoveryOptions> options)
{
    public IntegrationDetails Handle() => new(status.Current, options.Value.Host, options.Value.Port);
}
