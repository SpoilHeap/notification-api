using System.Net;
using System.Text.Json;
using NotificationApi.Application.Recipients;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NotificationApi.Web.Tests.Api;

/// <summary><c>GET /api/v1/recipients</c> with its revision ETag (spec §4.2, AC40, AC43, AC45).</summary>
public sealed class RecipientsEndpointTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task AC40_A_new_database_returns_the_revision_and_an_empty_list_with_the_revision_as_a_strong_etag()
    {
        await using var fresh = new NotificationApiFactory();
        using var client = fresh.CreateClient();

        using var response = await GetAsync(client, ifNoneMatch: null, await fresh.GetApiTokenAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["revision", "recipients"]);
        var revision = json.RootElement.GetProperty("revision").GetString()!;
        json.RootElement.GetProperty("recipients").GetArrayLength().ShouldBe(0);
        response.Headers.ETag.ShouldNotBeNull().IsWeak.ShouldBeFalse();
        response.Headers.ETag.Tag.ShouldBe($"\"{revision}\"");
    }

    [Fact]
    public async Task AC43_If_none_match_with_the_current_etag_is_304_with_no_body()
    {
        using var client = factory.CreateClient();
        var etag = await CurrentETagAsync(client);

        using var response = await GetAsync(client, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        response.Headers.ETag!.Tag.ShouldBe(etag);
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\"0\"")]
    [InlineData("\"not-the-revision\"")]
    public async Task AC43_A_different_etag_is_200(string ifNoneMatch)
    {
        using var client = factory.CreateClient();

        using var response = await GetAsync(client, ifNoneMatch);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Needs_the_token()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(TestRequests.Api(HttpMethod.Get, "/api/v1/recipients", null), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// AC43 and AC45 at the use-case level: a recipients change changes the revision, so the old ETag gets 200, and the
    /// <c>recipients.changed</c> event carries the revision the next GET returns. <c>RecipientsThroughUiTests</c>
    /// covers the same through the web UI.
    /// </summary>
    [Fact]
    public async Task AC43_AC45_After_a_change_the_old_etag_is_200_and_the_event_carries_the_new_revision()
    {
        await using var isolated = new NotificationApiFactory();
        using var ha = WireMockServer.Start();
        ha.Given(Request.Create().UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
        using var client = isolated.CreateClient();
        var token = await isolated.GetApiTokenAsync();
        (await client.PutWebhookAsync(token, ha.Url + "/api/webhook/abc", TestRequests.NewWebhookSecret())).Dispose();
        var before = await CurrentETagAsync(client, token);

        var revision = await isolated.Services.InScopeAsync<RecordRecipientsChange, string>(u => u.HandleAsync(TestContext.Current.CancellationToken));
        await TestRequests.WaitUntilAsync(() => ha.LogEntries.Any(), "the event is sent in the background");

        using var response = await GetAsync(client, before, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldBe($"\"{revision}\"");
        response.Headers.ETag.Tag.ShouldNotBe(before);
        using var json = JsonDocument.Parse(ha.LogEntries.ShouldHaveSingleItem().RequestMessage!.BodyAsBytes!);
        json.RootElement.GetProperty("type").GetString().ShouldBe("recipients.changed");
        json.RootElement.GetProperty("data").GetProperty("revision").GetString().ShouldBe(revision);
        using var after = await GetAsync(client, $"\"{revision}\"", token);
        after.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    private async Task<string> CurrentETagAsync(HttpClient client, string? token = null)
    {
        using var response = await GetAsync(client, ifNoneMatch: null, token);
        return response.Headers.ETag!.Tag;
    }

    private async Task<HttpResponseMessage> GetAsync(HttpClient client, string? ifNoneMatch, string? token = null)
    {
        using var request = TestRequests.Api(HttpMethod.Get, "/api/v1/recipients", token ?? await factory.GetApiTokenAsync());
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
