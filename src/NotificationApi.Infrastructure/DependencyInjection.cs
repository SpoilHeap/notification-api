using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Common;
using NotificationApi.Application.Discovery;
using NotificationApi.Infrastructure.Persistence;
using NotificationApi.Infrastructure.Security;
using NotificationApi.Infrastructure.Supervisor;
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
}
