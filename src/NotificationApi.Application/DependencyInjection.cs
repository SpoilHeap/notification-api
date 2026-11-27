using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Discovery;
using NotificationApi.Application.Events;
using NotificationApi.Application.Instances;
using NotificationApi.Application.Recipients;
using NotificationApi.Application.Webhooks;

namespace NotificationApi.Application;

public static class DependencyInjection
{
    /// <summary>Registers application use cases. Each use case is injected directly (no mediator).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<DiscoveryOptions>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<DiscoveryOptions>, DiscoveryOptionsValidator>();
        services.AddSingleton<DiscoveryStatusStore>();

        services.AddScoped<EnsureApiToken>();
        services.AddScoped<VerifyApiToken>();
        services.AddScoped<RevealApiToken>();
        services.AddScoped<PublishDiscovery>();
        services.AddScoped<GetIntegrationDetails>();

        services.AddScoped<EnsureInstance>();
        services.AddScoped<GetRecipients>();
        services.AddScoped<RecordRecipientsChange>();

        services.AddScoped<RegisterWebhook>();
        services.AddScoped<GetWebhook>();
        services.AddScoped<RemoveWebhook>();
        services.AddScoped<PublishHaEvent>();
        services.AddScoped<SendTestEvent>();
        return services;
    }
}
