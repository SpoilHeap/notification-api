using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NotificationApi.Application.Events;
using NotificationApi.Application.Webhooks;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

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

        // Change the last character to another valid one ('A' is itself a possible last character, so don't always use it).
        var wrongToken = token[..^1] + (token[^1] == 'A' ? 'E' : 'A');
        using (var wrong = await client.SendAsync(TestRequests.Api(HttpMethod.Get, "/api/v1/ping", wrongToken), TestContext.Current.CancellationToken))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        factory.Logs.Entries.ShouldNotBeEmpty();
        var prefix = token[..^1];
        factory.Logs.Entries.ShouldNotContain(e => e.Contains(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC74_Logs_never_contain_the_webhook_secret_the_webhook_path_or_event_bodies()
    {
        using var ha = WireMockServer.Start();
        ha.Given(Request.Create().UsingPost()).RespondWith(Response.Create().WithStatusCode(500));
        await using var factory = new NotificationApiFactory();
        await using var verbose = factory.WithWebHostBuilder(b => b.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace)));
        using var client = verbose.CreateClient();
        var token = await verbose.Services.GetApiTokenAsync();
        var secret = TestRequests.NewWebhookSecret();
        const string WebhookPath = "/api/webhook/5ecre7-webhook-id";

        (await client.PutWebhookAsync(token, ha.Url + WebhookPath, secret)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PutWebhookAsync(token, "not a url", secret)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var outcome = await verbose.Services.InScopeAsync<SendTestEvent, HaEventOutcome>(u => u.HandleAsync("user", TestContext.Current.CancellationToken));

        outcome.Kind.ShouldBe(HaEventOutcomeKind.Failed);
        factory.Logs.Entries.ShouldContain(e => e.StartsWith("Warning", StringComparison.Ordinal) && e.Contains(outcome.EventId, StringComparison.Ordinal) && e.Contains("500", StringComparison.Ordinal));
        factory.Logs.Entries.ShouldNotContain(e => e.Contains(secret, StringComparison.Ordinal));
        factory.Logs.Entries.ShouldNotContain(e => e.Contains(WebhookPath, StringComparison.Ordinal));
        factory.Logs.Entries.ShouldNotContain(e => e.Contains("\"occurred_at\"", StringComparison.Ordinal));
    }
}
