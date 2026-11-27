using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NotificationApi.Web.Tests.Api;

/// <summary>
/// The OpenAPI document is readable through Ingress only, and must match the checked-in contract
/// <c>docs/openapi.json</c> that the HA integration is built against (engineering_standards.md §2.5).
/// After an intended API change, regenerate it with <c>UPDATE_OPENAPI_CONTRACT=1 dotnet test</c>.
/// </summary>
public sealed class OpenApiTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    private const string DocumentPath = "/openapi/v1.json";

    [Fact]
    public async Task The_document_is_served_through_ingress_and_describes_the_api()
    {
        var document = await GetDocumentAsync();

        document.ShouldContain("\"/api/v1/ping\"");
    }

    [Theory]
    [InlineData(NotificationApiFactory.OtherIp)]
    [InlineData("172.30.33.4")] // another add-on, or HA Core, on the hassio network
    public async Task The_document_is_not_readable_from_other_addresses(string remoteIp)
    {
        using var client = factory.CreateClient();
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, DocumentPath, remoteIp);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_document_matches_the_checked_in_contract()
    {
        var actual = Normalise(await GetDocumentAsync());
        var contractPath = Path.Combine(RepoRoot(), "docs", "openapi.json");

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_CONTRACT") == "1")
        {
            await File.WriteAllTextAsync(contractPath, actual, TestContext.Current.CancellationToken);
        }

        File.Exists(contractPath).ShouldBeTrue($"{contractPath} is missing. Run: UPDATE_OPENAPI_CONTRACT=1 dotnet test");
        var expected = Normalise(await File.ReadAllTextAsync(contractPath, TestContext.Current.CancellationToken));
        actual.ShouldBe(expected, "The API contract changed. If intended, run UPDATE_OPENAPI_CONTRACT=1 dotnet test and commit docs/openapi.json.");
    }

    private async Task<string> GetDocumentAsync()
    {
        using var client = factory.CreateClient();
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, DocumentPath);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Stable formatting, and no <c>servers</c> block (it depends on the request's host).</summary>
    private static string Normalise(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("servers");
        return node.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NotificationApi.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root (NotificationApi.slnx) not found.");
    }
}
