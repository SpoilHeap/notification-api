using System.Net;
using NotificationApi.Application.Discovery;

namespace NotificationApi.Web.Hosting;

internal static class DiscoveryExtensions
{
    public static IServiceCollection AddDiscovery(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DiscoveryOptions>()
            .Bind(configuration.GetSection(DiscoveryOptions.SectionName))
            .PostConfigure(o =>
            {
                // The Supervisor sets the container hostname; HA Core resolves it on the hassio network.
                if (string.IsNullOrWhiteSpace(o.Host))
                {
                    o.Host = Dns.GetHostName();
                }
            });

        services.AddHostedService<DiscoveryHostedService>();
        return services;
    }
}
