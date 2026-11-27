using System.Net;
using System.Text.RegularExpressions;
using NotificationApi.Application.Discovery;

namespace NotificationApi.Web.Tests.Components;

/// <summary>The manual-setup fallback page (spec §9.4).</summary>
public sealed partial class IntegrationPageTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task Shows_that_discovery_was_skipped_outside_ha_os_and_the_manual_setup_values()
    {
        using var client = factory.CreateClient();
        (await factory.Services.WaitForDiscoveryAsync()).State.ShouldBe(DiscoveryState.Skipped);

        var html = await GetPageAsync(client);

        html.ShouldContain("<dd data-testid=\"discovery-state\">Skipped</dd>");
        html.ShouldContain("SUPERVISOR_TOKEN");
        html.ShouldContain("<code>notification-api-test</code>");
        html.ShouldContain("<code>8080</code>");
        html.ShouldContain("Reveal API token");
        html.ShouldNotContain(await factory.GetApiTokenAsync());
    }

    [Fact]
    public async Task Revealing_shows_the_stored_token_and_disables_caching()
    {
        using var client = factory.CreateClient();
        var form = ParseForm(await GetPageAsync(client));

        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Post, "/integration");
        request.Content = new FormUrlEncodedContent(form);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var token = await factory.GetApiTokenAsync();
        html.ShouldContain($"<code data-testid=\"api-token\">{token}</code>");
        factory.Logs.Entries.ShouldContain(e => e.Contains("revealed", StringComparison.Ordinal) && e.Contains(NotificationApiFactory.HaUserId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC83_Reveal_without_an_antiforgery_token_is_rejected()
    {
        using var client = factory.CreateClient();
        var form = ParseForm(await GetPageAsync(client)).Where(f => f.Key != "__RequestVerificationToken");

        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Post, "/integration");
        request.Content = new FormUrlEncodedContent(form);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain(await factory.GetApiTokenAsync());
    }

    [Fact]
    public async Task AC80_The_page_is_not_reachable_outside_ingress()
    {
        using var client = factory.CreateClient();
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, "/integration", NotificationApiFactory.OtherIp);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<string> GetPageAsync(HttpClient client)
    {
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, "/integration");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The hidden fields Blazor renders in the form: the antiforgery token and the form's handler name.</summary>
    private static Dictionary<string, string> ParseForm(string html)
    {
        var fields = HiddenInput().Matches(html).ToDictionary(m => m.Groups["name"].Value, m => WebUtility.HtmlDecode(m.Groups["value"].Value));
        fields.ShouldContainKey("__RequestVerificationToken");
        fields.ShouldContainKey("_handler");
        return fields;
    }

    [GeneratedRegex("<input type=\"hidden\" name=\"(?<name>[^\"]+)\" value=\"(?<value>[^\"]*)\"")]
    private static partial Regex HiddenInput();
}
