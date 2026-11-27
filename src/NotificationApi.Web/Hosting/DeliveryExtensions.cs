using NotificationApi.Application.Notifications;

namespace NotificationApi.Web.Hosting;

internal static class DeliveryExtensions
{
    /// <summary>Retry options (<c>Delivery:MaxRetries</c>, <c>Delivery:RetryInterval</c>) and the delivery worker.</summary>
    public static IServiceCollection AddDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DeliveryOptions>().Bind(configuration.GetSection(DeliveryOptions.SectionName));
        services.AddHostedService<DeliveryWorkerService>();
        return services;
    }
}
