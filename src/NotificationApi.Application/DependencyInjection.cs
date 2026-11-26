using Microsoft.Extensions.DependencyInjection;

namespace NotificationApi.Application;

public static class DependencyInjection
{
    /// <summary>Registers application use cases. Each use case is injected directly (no mediator).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
