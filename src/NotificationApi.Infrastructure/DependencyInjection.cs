using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Discovery;
using NotificationApi.Application.Events;
using NotificationApi.Application.Instances;
using NotificationApi.Application.Recipients;
using NotificationApi.Infrastructure.Events;
using NotificationApi.Infrastructure.Persistence;
using NotificationApi.Infrastructure.Security;
using NotificationApi.Infrastructure.Supervisor;
using NotificationApi.Infrastructure.Webhooks;
using Polly;

namespace NotificationApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        AddPersistence(services);
        AddSecretProtection(services);
        AddSupervisor(services, configuration);
        AddWebhook(services, configuration);
        return services;
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddDbContext<NotificationDbContext>((sp, options) =>
        {
            var storage = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            Directory.CreateDirectory(storage.DataPath);
            options.UseSqlite($"Data Source={storage.DatabasePath}");
        });

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IApiClientRepository, ApiClientRepository>();
        services.AddScoped<IInstanceRepository, InstanceRepository>();
        services.AddScoped<IContactRepository, ContactRepository>();
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
    }

    /// <summary>Data Protection keys live in /data/keys, so encrypted secrets and antiforgery tokens survive restarts, updates and backup restores.</summary>
    private static void AddSecretProtection(IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("NotificationApi");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<StorageOptions>, ILoggerFactory>((keys, storage, loggerFactory) =>
                keys.XmlRepository = new FileSystemXmlRepository(Directory.CreateDirectory(storage.Value.KeysPath), loggerFactory));

        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
    }

    private static void AddSupervisor(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SupervisorOptions>()
            .Bind(configuration.GetSection(SupervisorOptions.SectionName))
            .PostConfigure(o =>
            {
                if (string.IsNullOrWhiteSpace(o.Token))
                {
                    o.Token = configuration[SupervisorOptions.TokenEnvironmentVariable];
                }
            })
            .Validate(o => o.BaseUrl is { IsAbsoluteUri: true } url && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps),
                "Supervisor:BaseUrl must be an absolute http(s) URL.")
            .Validate(o => o.RequestTimeout > TimeSpan.Zero, "Supervisor:RequestTimeout must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<IDiscoveryPublisher, SupervisorDiscoveryPublisher>((sp, client) =>
            {
                var baseUrl = sp.GetRequiredService<IOptions<SupervisorOptions>>().Value.BaseUrl.ToString();
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            })
            .AddResilienceHandler("supervisor", (pipeline, context) =>
                pipeline.AddTimeout(context.ServiceProvider.GetRequiredService<IOptions<SupervisorOptions>>().Value.RequestTimeout));
    }

    /// <summary>
    /// HA webhook events, queued in process and sent in the background (see <see cref="ChannelPendingHaEvents"/>): one attempt with a timeout and no retry (AC34). The default HttpClient logging is removed
    /// because it writes the full URL, whose path holds HA's webhook id; failures are logged by <c>PublishHaEvent</c>.
    /// </summary>
    private static void AddWebhook(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPendingHaEvents, ChannelPendingHaEvents>();
        services.AddOptions<WebhookOptions>()
            .Bind(configuration.GetSection(WebhookOptions.SectionName))
            .Validate(o => o.RequestTimeout > TimeSpan.Zero, "Webhook:RequestTimeout must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<IHaEventPublisher, WebhookEventPublisher>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers()
            .AddResilienceHandler("webhook", (pipeline, context) =>
                pipeline.AddTimeout(context.ServiceProvider.GetRequiredService<IOptions<WebhookOptions>>().Value.RequestTimeout));
    }
}
