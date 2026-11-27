using System.Net;
using System.Net.Http.Json;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Web.Tests.Authentication;

/// <summary>The ApiToken scheme on <c>/api/v1</c> (spec §8.3, AC11).</summary>
public sealed class ApiTokenAuthenticationTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task AC11_Valid_bearer_token_reaches_the_api()
    {
        var token = await factory.GetApiTokenAsync();

        using var response = await SendAsync(TestRequests.Api(HttpMethod.Get, "/api/v1/ping", token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<PingBody>(TestContext.Current.CancellationToken)).ShouldNotBeNull();
        body.Ok.ShouldBeTrue();
        body.Version.ShouldNotBeNullOrWhiteSpace();
        body.Version.ShouldNotBe("unknown");
    }

    [Fact]
    public async Task The_bearer_scheme_is_case_insensitive()
    {
        var token = await factory.GetApiTokenAsync();
        using var request = TestRequests.Api(HttpMethod.Get, "/api/v1/ping", bearerToken: null);
        request.Headers.TryAddWithoutValidation("Authorization", $"bearer {token}");

        using var response = await SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AC11_Missing_token_returns_401_with_a_bearer_challenge()
    {
        using var response = await SendAsync(TestRequests.Api(HttpMethod.Get, "/api/v1/ping", bearerToken: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    public static TheoryData<string> WrongTokens => new()
    {
        ApiToken.Generate().Value, // well-formed, but not ours
        "not-a-token",
        "A",
    };

    [Theory]
    [MemberData(nameof(WrongTokens))]
    public async Task AC11_Wrong_token_returns_401_invalid_token(string token)
    {
        using var response = await SendAsync(TestRequests.Api(HttpMethod.Get, "/api/v1/ping", token));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldContain("invalid_token");
    }

    [Fact]
    public async Task AC11_Real_token_with_a_suffix_is_rejected()
    {
        var token = await factory.GetApiTokenAsync();

        using var response = await SendAsync(TestRequests.Api(HttpMethod.Get, "/api/v1/ping", token + "x"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC11_Other_authorization_schemes_are_not_accepted()
    {
        var token = await factory.GetApiTokenAsync();
        using var request = TestRequests.Api(HttpMethod.Get, "/api/v1/ping", bearerToken: null);
        request.Headers.TryAddWithoutValidation("Authorization", $"Basic {token}");

        using var response = await SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC11_A_token_in_the_query_string_is_not_accepted()
    {
        var token = await factory.GetApiTokenAsync();

        using var response = await SendAsync(TestRequests.Api(HttpMethod.Get, $"/api/v1/ping?access_token={token}", bearerToken: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC11_An_ingress_identity_is_not_an_api_credential()
    {
        using var response = await SendAsync(NotificationApiFactory.IngressRequest(HttpMethod.Get, "/api/v1/ping"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using var client = factory.CreateClient();
        using (request)
        {
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }
    }

    private sealed record PingBody(bool Ok, string Version);
}
