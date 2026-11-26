using System.Net;

namespace NotificationApi.Web.Tests;

// Serving _framework/blazor.web.js is checked against the real published image in CI
// (scripts/smoke-test-image.sh). The spike bug was in the Docker build output, which an
// in-process WebApplicationFactory test can't see.
public sealed class HostingTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task Health_endpoint_returns_200()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Home_page_references_the_blazor_script_and_stylesheet()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        html.ShouldMatch("<script src=\"_framework/blazor\\.web(\\.[a-z0-9]+)?\\.js\"></script>");
        html.ShouldMatch("<link rel=\"stylesheet\" href=\"app(\\.[a-z0-9]+)?\\.css\" />");
    }
}
