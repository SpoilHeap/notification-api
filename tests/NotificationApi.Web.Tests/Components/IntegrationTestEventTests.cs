using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NotificationApi.Web.Tests.Components;

/// <summary>The Integration page's webhook section and its <strong>Send test event</strong> button.</summary>
public sealed class IntegrationTestEventTests : IAsyncLifetime
{
    private readonly NotificationApiFactory _factory = new();
    private readonly WireMockServer _ha = WireMockServer.Start();
    private HttpClient _client = default!;

    public ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _ha.Stop();
        _ha.Dispose();
    }

    [Fact]
    public async Task Without_a_webhook_the_page_says_so_and_offers_no_test_button()
    {
        var html = await IntegrationPageTests.GetPageAsync(_client);

        html.ShouldContain("<dd data-testid=\"webhook-state\">Not registered.");
        html.ShouldNotContain("Send test event");
    }

    [Fact]
    public async Task The_page_shows_the_registered_webhook_but_not_its_secret()
    {
        var secret = TestRequests.NewWebhookSecret();
        await RegisterAsync(secret);

        var html = await IntegrationPageTests.GetPageAsync(_client);

        html.ShouldContain("<dd data-testid=\"webhook-state\">Registered</dd>");
        html.ShouldContain($"<code data-testid=\"webhook-url\">{_ha.Url}/api/webhook/abc</code>");
        html.ShouldContain("Send test event");
        html.ShouldNotContain(secret, Case.Sensitive);
    }

    [Fact]
    public async Task Sending_a_test_event_shows_the_http_status()
    {
        _ha.Given(Request.Create().UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
        await RegisterAsync(TestRequests.NewWebhookSecret());

        var html = await PostTestEventAsync();

        var sent = _ha.LogEntries.ShouldHaveSingleItem().RequestMessage!;
        sent.Body!.ShouldContain("\"type\":\"webhook.test\"");
        html.ShouldMatch("<p class=\"ok\" data-testid=\"test-event-result\">Test event evt_[0-9a-f]{32} delivered: Home Assistant answered HTTP 200.</p>");
        _factory.Logs.Entries.ShouldContain(e => e.Contains("sent test event", StringComparison.Ordinal) && e.Contains(NotificationApiFactory.HaUserId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failing_webhook_shows_the_error()
    {
        _ha.Given(Request.Create().UsingPost()).RespondWith(Response.Create().WithStatusCode(404));
        await RegisterAsync(TestRequests.NewWebhookSecret());

        var html = await PostTestEventAsync();

        html.ShouldMatch("<p class=\"error\" data-testid=\"test-event-result\">Test event evt_[0-9a-f]{32} was not delivered: the webhook answered HTTP 404.</p>");
    }

    [Fact]
    public async Task An_unreachable_webhook_shows_the_error()
    {
        (await _client.PutWebhookAsync(await _factory.GetApiTokenAsync(), "http://127.0.0.1:1/api/webhook/abc", TestRequests.NewWebhookSecret())).Dispose();

        var html = await PostTestEventAsync();

        html.ShouldContain("was not delivered: the webhook is unreachable");
    }

    [Fact]
    public async Task AC83_Sending_a_test_event_without_an_antiforgery_token_is_rejected()
    {
        _ha.Given(Request.Create().UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
        await RegisterAsync(TestRequests.NewWebhookSecret());
        var form = IntegrationPageTests.ParseForm(await IntegrationPageTests.GetPageAsync(_client), "send-test-event")
            .Where(f => f.Key != "__RequestVerificationToken");

        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Post, "/integration");
        request.Content = new FormUrlEncodedContent(form);
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _ha.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC80_The_test_event_cannot_be_sent_from_outside_ingress()
    {
        _ha.Given(Request.Create().UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
        await RegisterAsync(TestRequests.NewWebhookSecret());
        var form = IntegrationPageTests.ParseForm(await IntegrationPageTests.GetPageAsync(_client), "send-test-event");

        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Post, "/integration", NotificationApiFactory.OtherIp);
        request.Content = new FormUrlEncodedContent(form);
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _ha.LogEntries.ShouldBeEmpty();
    }

    private async Task RegisterAsync(string secret)
    {
        using var response = await _client.PutWebhookAsync(await _factory.GetApiTokenAsync(), _ha.Url + "/api/webhook/abc", secret);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<string> PostTestEventAsync()
    {
        var form = IntegrationPageTests.ParseForm(await IntegrationPageTests.GetPageAsync(_client), "send-test-event");
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Post, "/integration");
        request.Content = new FormUrlEncodedContent(form);
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
