using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace NotificationApi.Web.Tests.Ingress;

public sealed class DevFakeIngressGuardTests
{
    [Fact]
    public async Task Startup_fails_if_fake_ingress_is_enabled_outside_development()
    {
        // The fake Ingress headers bypass the proxy check, so they must never run in Production
        // (engineering_standards.md §9).
        await using var factory = new NotificationApiFactory()
            .WithWebHostBuilder(b => b.UseSetting("Ingress:DevFakeIngress", "true"));

        var ex = Should.Throw<OptionsValidationException>(() => factory.CreateClient());
        ex.Message.ShouldContain("DevFakeIngress");
    }

    [Fact]
    public async Task In_development_fake_ingress_trusts_any_address_and_applies_the_ingress_path()
    {
        await using var factory = new NotificationApiFactory()
            .WithWebHostBuilder(b => b
                .UseEnvironment("Development")
                .UseSetting("Ingress:DevFakeIngress", "true"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(NotificationApiFactory.RemoteIpHeader, "192.168.1.50");
        request.Headers.Add("X-Ingress-Path", "/api/hassio_ingress/dev");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain("<base href=\"/api/hassio_ingress/dev/\" />");
    }
}
