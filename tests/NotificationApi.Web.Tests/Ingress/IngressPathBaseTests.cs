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
    public async Task Ingress_path_header_from_any_other_address_is_ignored()
    {
        var html = await GetHomeAsync("192.168.1.50", Prefix);

        html.ShouldContain("<base href=\"/\" />");
        html.ShouldNotContain(Prefix);
    }

    [Fact]
    public async Task Request_from_ingress_proxy_without_a_path_uses_root()
    {
        var html = await GetHomeAsync(NotificationApiFactory.IngressProxyIp, ingressPath: null);

        html.ShouldContain("<base href=\"/\" />");
    }

    private async Task<string> GetHomeAsync(string remoteIp, string? ingressPath)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(NotificationApiFactory.RemoteIpHeader, remoteIp);
        if (ingressPath is not null)
        {
            request.Headers.Add("X-Ingress-Path", ingressPath);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
