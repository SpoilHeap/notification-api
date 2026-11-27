using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Common;
using NotificationApi.Application.Discovery;
using NotificationApi.Application.Tests.Fakes;

namespace NotificationApi.Application.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task Use_cases_resolve_once_the_ports_are_registered()
    {
        var clients = new InMemoryApiClients();
        await using var provider = new ServiceCollection()
            .AddApplication()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IApiClientRepository>(clients)
            .AddSingleton<IUnitOfWork>(clients)
            .AddSingleton<ISecretProtector, FakeSecretProtector>()
            .AddSingleton<IDiscoveryPublisher, FakeDiscoveryPublisher>()
            .Configure<DiscoveryOptions>(o => o.Host = "addon-host")
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<EnsureApiToken>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<VerifyApiToken>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<RevealApiToken>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<GetIntegrationDetails>().ShouldNotBeNull();
        var status = await scope.ServiceProvider.GetRequiredService<PublishDiscovery>().HandleAsync(TestContext.Current.CancellationToken);

        status.State.ShouldBe(DiscoveryState.Published);
        provider.GetRequiredService<TimeProvider>().ShouldBe(TimeProvider.System);
    }

    [Fact]
    public void Invalid_discovery_options_are_rejected()
    {
        using var provider = new ServiceCollection()
            .AddApplication()
            .BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<DiscoveryOptions>>().Value)
            .Message.ShouldContain("Discovery:Host");
    }
}
