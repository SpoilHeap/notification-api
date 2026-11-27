using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NotificationApi.Application.Events;
using NotificationApi.Application.Webhooks;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NotificationApi.Web.Tests.Api;

/// <summary><c>PUT/GET/DELETE /api/v1/webhook</c> (spec §4.3) and the events that follow a registration.</summary>
public sealed class WebhookEndpointTests : IAsyncLifetime
{
    private readonly NotificationApiFactory _factory = new();
    private readonly WireMockServer _ha = WireMockServer.Start();
    private HttpClient _client = default!;
    private string _token = default!;

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        _token = await _factory.GetApiTokenAsync();
        _ha.Given(Request.Create().UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _ha.Stop();
        _ha.Dispose();
    }

    [Fact]
    public async Task AC35_Put_returns_204_and_get_shows_the_url_and_time_but_never_the_secret()
    {
        var secret = TestRequests.NewWebhookSecret();
        var url = _ha.Url + "/api/webhook/first";

        using (var put = await _client.PutWebhookAsync(_token, url, secret))
        {
            put.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var get = await SendAsync(HttpMethod.Get);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await get.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.ShouldNotContain(secret, Case.Sensitive);
        using var json = JsonDocument.Parse(text);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["url", "registered_at"]);
        json.RootElement.GetProperty("url").GetString().ShouldBe(url);
        var registeredAt = json.RootElement.GetProperty("registered_at").GetString()!;
        registeredAt.ShouldEndWith("Z");
        DateTimeOffset.Parse(registeredAt, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AC35_Events_go_to_the_registered_url_signed_with_the_secret()
    {
        var secret = TestRequests.NewWebhookSecret();
        (await _client.PutWebhookAsync(_token, _ha.Url + "/api/webhook/first", secret)).Dispose();

        var outcome = await SendTestEventAsync();

        outcome.Kind.ShouldBe(HaEventOutcomeKind.Delivered);
        var request = _ha.LogEntries.ShouldHaveSingleItem().RequestMessage!;
        request.Path.ShouldBe("/api/webhook/first");
        var timestamp = request.Headers![WebhookSignature.TimestampHeader].Single();
        var expected = "sha256=" + Convert.ToHexStringLower(
            HMACSHA256.HashData(Convert.FromBase64String(secret), (byte[])[.. Encoding.ASCII.GetBytes(timestamp + "."), .. request.BodyAsBytes!]));
        request.Headers[WebhookSignature.SignatureHeader].ShouldBe([expected]);
        using var body = JsonDocument.Parse(request.BodyAsBytes!);
        body.RootElement.GetProperty("id").GetString().ShouldBe(outcome.EventId);
        body.RootElement.GetProperty("type").GetString().ShouldBe("webhook.test");
        body.RootElement.GetProperty("data").ToString().ShouldBe("{}");
    }

    [Fact]
    public async Task AC35_A_second_put_replaces_the_first_and_delete_stops_events()
    {
        (await _client.PutWebhookAsync(_token, _ha.Url + "/api/webhook/first", TestRequests.NewWebhookSecret())).Dispose();
        (await _client.PutWebhookAsync(_token, _ha.Url + "/api/webhook/second", TestRequests.NewWebhookSecret())).Dispose();

        await SendTestEventAsync();
        _ha.LogEntries.ShouldHaveSingleItem().RequestMessage!.Path.ShouldBe("/api/webhook/second");

        using (var delete = await SendAsync(HttpMethod.Delete))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await SendTestEventAsync()).Kind.ShouldBe(HaEventOutcomeKind.NotRegistered);
        _ha.LogEntries.Count.ShouldBe(1);
        using var get = await SendAsync(HttpMethod.Get);
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        get.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Get_is_404_and_delete_is_204_when_nothing_is_registered()
    {
        using (var get = await SendAsync(HttpMethod.Get))
        {
            get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using var delete = await SendAsync(HttpMethod.Delete);
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("/api/webhook/relative", "url")]
    [InlineData("ftp://homeassistant/api/webhook/x", "url")]
    [InlineData("homeassistant:8123/api/webhook/x", "url")]
    public async Task AC36_An_invalid_url_is_400_with_a_field_error_and_keeps_the_previous_registration(string url, string field)
    {
        var original = _ha.Url + "/api/webhook/original";
        (await _client.PutWebhookAsync(_token, original, TestRequests.NewWebhookSecret())).Dispose();

        using var response = await _client.PutWebhookAsync(_token, url, TestRequests.NewWebhookSecret());

        await ShouldBeValidationProblemAsync(response, field);
        (await RegisteredUrlAsync()).ShouldBe(original);
    }

    [Fact]
    public async Task AC36_A_secret_shorter_than_32_bytes_is_400_and_keeps_the_previous_registration()
    {
        var original = _ha.Url + "/api/webhook/original";
        (await _client.PutWebhookAsync(_token, original, TestRequests.NewWebhookSecret())).Dispose();

        using var response = await _client.PutWebhookAsync(_token, _ha.Url + "/api/webhook/new", Convert.ToBase64String(new byte[31]));

        await ShouldBeValidationProblemAsync(response, "secret");
        (await RegisteredUrlAsync()).ShouldBe(original);
    }

    [Fact]
    public async Task AC36_A_secret_that_is_not_base64_is_400()
    {
        using var response = await _client.PutWebhookAsync(_token, _ha.Url + "/api/webhook/new", "not base64, and long enough to be 32 characters");

        await ShouldBeValidationProblemAsync(response, "secret");
    }

    [Fact]
    public async Task An_empty_body_is_400_with_both_field_errors()
    {
        using var request = TestRequests.Api(HttpMethod.Put, "/api/v1/webhook", _token);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        var errors = await ShouldBeValidationProblemAsync(response, "url");
        errors.TryGetProperty("secret", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Malformed_json_is_400()
    {
        using var request = TestRequests.Api(HttpMethod.Put, "/api/v1/webhook", _token);
        request.Content = new StringContent("{not json", Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Every_webhook_route_needs_the_token(string method)
    {
        using var response = await _client.SendAsync(TestRequests.Api(new HttpMethod(method), "/api/v1/webhook", null), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<string?> RegisteredUrlAsync()
    {
        using var get = await SendAsync(HttpMethod.Get);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("url").GetString();
    }

    private static async Task<JsonElement> ShouldBeValidationProblemAsync(HttpResponseMessage response, string field)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var errors = json.RootElement.GetProperty("errors");
        errors.GetProperty(field).GetArrayLength().ShouldBe(1);
        return errors.Clone();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method) =>
        _client.SendAsync(TestRequests.Api(method, "/api/v1/webhook", _token), TestContext.Current.CancellationToken);

    private Task<HaEventOutcome> SendTestEventAsync() =>
        _factory.Services.InScopeAsync<SendTestEvent, HaEventOutcome>(u => u.HandleAsync(NotificationApiFactory.HaUserId, TestContext.Current.CancellationToken));
}
