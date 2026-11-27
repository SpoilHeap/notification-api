using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace NotificationApi.Web.Tests.Hosting;

public sealed class SecretLoggingTests
{
    [Fact]
    public async Task AC74_Logs_never_contain_the_api_token()
    {
        await using var factory = new NotificationApiFactory();
        // Log everything, including framework Debug/Trace output, so nothing can hide below the default level.
        await using var verbose = factory.WithWebHostBuilder(b => b.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace)));
        using var client = verbose.CreateClient();
        await verbose.Services.WaitForDiscoveryAsync();
        var token = await verbose.Services.GetApiTokenAsync();

        using (var ok = await client.SendAsync(TestRequests.Api(HttpMethod.Get, "/api/v1/ping", token), TestContext.Current.CancellationToken))
        {
            ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var wrong = await client.SendAsync(TestRequests.Api(HttpMethod.Get, "/api/v1/ping", token[..^1] + "A"), TestContext.Current.CancellationToken))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        factory.Logs.Entries.ShouldNotBeEmpty();
        var prefix = token[..^1];
        factory.Logs.Entries.ShouldNotContain(e => e.Contains(prefix, StringComparison.Ordinal));
    }
}
