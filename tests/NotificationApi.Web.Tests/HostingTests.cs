using System.Net;

namespace NotificationApi.Web.Tests;

// Serving _framework/blazor.web.js is checked against the real published image in CI
// (scripts/smoke-test-image.sh). The spike bug was in the Docker build output, which an
// in-process WebApplicationFactory test can't see.
public sealed class HostingTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Theory]
    [InlineData(null)]
    [InlineData(NotificationApiFactory.OtherIp)]
    public async Task Health_endpoint_is_anonymous(string? remoteIp)
    {
        // The Docker HEALTHCHECK probes /health from inside the container, with no Ingress headers.
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        if (remoteIp is not null)
        {
            request.Headers.Add(NotificationApiFactory.RemoteIpHeader, remoteIp);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Home_page_references_the_blazor_script_and_stylesheet()
    {
        using var client = factory.CreateClient();
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, "/");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        html.ShouldMatch("<script src=\"_framework/blazor\\.web(\\.[a-z0-9]+)?\\.js\"></script>");
        html.ShouldMatch("<link rel=\"stylesheet\" href=\"app(\\.[a-z0-9]+)?\\.css\" />");
        html.ShouldContain("href=\"integration\"");
    }

    [Fact]
    public async Task The_database_is_migrated_on_startup()
    {
        using var client = factory.CreateClient();

        File.Exists(Path.Combine(factory.DataPath, "notification-api.db")).ShouldBeTrue();
        factory.Logs.Entries.ShouldContain(e => e.Contains("database migration", StringComparison.Ordinal) || e.Contains("up to date", StringComparison.Ordinal));
    }
}
