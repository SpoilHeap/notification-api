using System.Net;

namespace NotificationApi.Web.Tests.Authentication;

/// <summary>The Ingress scheme over the whole pipeline (spec §8.1, engineering_standards.md §4.3).</summary>
public sealed class IngressAuthenticationTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Theory]
    [InlineData(NotificationApiFactory.IngressProxyIp)]
    [InlineData("::ffff:172.30.32.2")] // Kestrel dual-stack (spec §13)
    public async Task AC80_UI_request_from_the_ingress_proxy_with_identity_headers_is_served(string remoteIp)
    {
        using var response = await SendAsync(NotificationApiFactory.IngressRequest(HttpMethod.Get, "/", remoteIp));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(NotificationApiFactory.OtherIp)]
    [InlineData("172.30.32.3")]
    [InlineData("172.30.32.1")] // the hassio network gateway
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:192.168.1.50")]
    [InlineData("fe80::1")]
    public async Task AC80_UI_request_from_any_other_address_is_rejected_even_with_valid_headers(string remoteIp)
    {
        using var response = await SendAsync(NotificationApiFactory.IngressRequest(HttpMethod.Get, "/", remoteIp));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("Home Assistant");
    }

    [Fact]
    public async Task AC80_UI_request_without_a_remote_address_is_rejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Remote-User-Id", NotificationApiFactory.HaUserId);

        using var response = await SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UI_request_from_the_proxy_without_identity_headers_is_rejected()
    {
        using var response = await SendAsync(NotificationApiFactory.IngressRequest(HttpMethod.Get, "/", withUser: false));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UI_request_from_the_proxy_with_an_empty_user_id_is_rejected()
    {
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, "/", withUser: false);
        request.Headers.Add("X-Remote-User-Id", " ");

        using var response = await SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(NotificationApiFactory.IngressProxyIp, HttpStatusCode.OK)]
    [InlineData(NotificationApiFactory.OtherIp, HttpStatusCode.Forbidden)]
    public async Task Static_assets_are_served_only_through_ingress(string remoteIp, HttpStatusCode expected)
    {
        using var response = await SendAsync(NotificationApiFactory.IngressRequest(HttpMethod.Get, "/app.css", remoteIp));

        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task An_unknown_path_through_ingress_is_denied_by_the_fallback_policy()
    {
        using var response = await SendAsync(NotificationApiFactory.IngressRequest(HttpMethod.Get, "/no-such-page"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using var client = factory.CreateClient();
        using (request)
        {
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }
    }
}
