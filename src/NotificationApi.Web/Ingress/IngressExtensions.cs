using Microsoft.Extensions.Options;

namespace NotificationApi.Web.Ingress;

internal static partial class IngressExtensions
{
    public static IServiceCollection AddIngress(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<IngressOptions>()
            .Bind(configuration.GetSection(IngressOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => System.Net.IPAddress.TryParse(o.TrustedProxyAddress, out _), "Ingress:TrustedProxyAddress must be an IP address.")
            .Validate(o => !o.DevFakeIngress || environment.IsDevelopment(), "Ingress:DevFakeIngress may only be enabled in the Development environment.")
            .ValidateOnStart();

        return services;
    }

    public static IApplicationBuilder UseIngress(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<IngressOptions>>().Value;
        if (options.DevFakeIngress)
        {
            LogDevFakeIngressEnabled(app.ApplicationServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(IngressExtensions)));
        }

        return app.UseMiddleware<IngressMiddleware>();
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Ingress:DevFakeIngress is enabled: every request is treated as coming from HA Ingress.")]
    private static partial void LogDevFakeIngressEnabled(ILogger logger);
}
