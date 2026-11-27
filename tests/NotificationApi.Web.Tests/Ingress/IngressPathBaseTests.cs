using System.Net;

namespace NotificationApi.Web.Tests.Ingress;

public sealed class IngressPathBaseTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    private const string Prefix = "/api/hassio_ingress/abc123";

    [Theory]
    [InlineData(NotificationApiFactory.IngressProxyIp)]
    [InlineData("::ffff:172.30.32.2")] // Kestrel dual-stack: IPv4-mapped IPv6 (spec §13)
    public async Task Request_from_ingress_proxy_uses_the_ingress_path_as_base_href(string remoteIp)
    {
        var html = await GetHomeAsync(remoteIp, Prefix);

        html.ShouldContain($"<base href=\"{Prefix}/\" />");
    }

    [Fact]
    public async Task Request_from_ingress_proxy_without_a_path_uses_root()
    {
        var html = await GetHomeAsync(NotificationApiFactory.IngressProxyIp, ingressPath: null);

        html.ShouldContain("<base href=\"/\" />");
    }

    [Fact]
    public async Task Ingress_path_header_from_any_other_address_is_ignored_and_the_request_rejected()
    {
        using var client = factory.CreateClient();
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, "/", NotificationApiFactory.OtherIp);
        request.Headers.Add("X-Ingress-Path", Prefix);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain(Prefix);
    }

    [Fact]
    public async Task Static_assets_are_served_through_ingress()
    {
        // Ingress strips its prefix before proxying, so the add-on sees /app.css plus X-Ingress-Path.
        using var client = factory.CreateClient();
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, "/app.css");
        request.Headers.Add("X-Ingress-Path", Prefix);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/css");
    }

    private async Task<string> GetHomeAsync(string remoteIp, string? ingressPath)
    {
        using var client = factory.CreateClient();
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, "/", remoteIp);
        if (ingressPath is not null)
        {
            request.Headers.Add("X-Ingress-Path", ingressPath);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
