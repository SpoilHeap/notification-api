using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace NotificationApi.Web.Tests.Authentication;

/// <summary>
/// Fallback deny policy (engineering_standards.md §2.5, §4.3, §6): enumerates every endpoint the app maps, so a new
/// endpoint can't slip in without an explicit policy.
/// </summary>
public sealed class EndpointAuthorizationTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    /// <summary>The only endpoints that may be anonymous.</summary>
    private static readonly string[] AnonymousRoutes = ["/health"];

    [Fact]
    public void Every_endpoint_declares_its_authorization_explicitly()
    {
        var endpoints = Endpoints();

        var undeclared = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            // MapStaticAssets adds a catch-all for files missing from the *build* manifest (dotnet run / tests only, not in the
            // published app). It carries no metadata of its own, so the deny fallback policy covers it; the request test checks that.
            .Where(e => !(e.DisplayName == "Fallback {**path:file}" && e.Order == int.MaxValue))
            .Select(Describe)
            .ToList();

        endpoints.Count.ShouldBeGreaterThan(3);
        undeclared.ShouldBeEmpty();
    }

    [Fact]
    public void Only_health_is_anonymous()
    {
        var anonymous = Endpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Distinct()
            .ToList();

        anonymous.ShouldBe(AnonymousRoutes, ignoreOrder: true);
    }

    [Fact]
    public async Task AC11_Unauthenticated_requests_to_every_endpoint_are_rejected()
    {
        using var client = factory.CreateClient();
        var failures = new List<string>();

        foreach (var (method, path) in Requests())
        {
            var isApi = path.StartsWith("/api/", StringComparison.Ordinal);
            var expected = AnonymousRoutes.Contains(path) ? HttpStatusCode.OK
                : isApi ? HttpStatusCode.Unauthorized
                : HttpStatusCode.Forbidden; // UI: only reachable through Ingress

            // An API call with no token, and a UI call that doesn't come from the Ingress proxy.
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.Add(NotificationApiFactory.RemoteIpHeader, NotificationApiFactory.OtherIp);
            request.Headers.Add("X-Remote-User-Id", NotificationApiFactory.HaUserId);
            if (method is "POST" or "PUT" or "PATCH")
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            if (response.StatusCode != expected)
            {
                failures.Add($"{method} {path}: {(int)response.StatusCode}, expected {(int)expected}");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC11_Api_endpoints_reject_ingress_callers_without_a_token()
    {
        using var client = factory.CreateClient();
        var failures = new List<string>();

        foreach (var (method, path) in Requests().Where(r => r.Path.StartsWith("/api/", StringComparison.Ordinal)))
        {
            using var request = NotificationApiFactory.IngressRequest(new HttpMethod(method), path);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                failures.Add($"{method} {path}: {(int)response.StatusCode}");
            }
        }

        failures.ShouldBeEmpty();
    }

    private List<RouteEndpoint> Endpoints()
    {
        using var client = factory.CreateClient(); // starts the host
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
    }

    private List<(string Method, string Path)> Requests() =>
        Endpoints()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => (m, ToPath(e.RoutePattern))))
            .Distinct()
            .ToList();

    /// <summary>A concrete path for a route pattern, with a sample value for each parameter.</summary>
    private static string ToPath(RoutePattern pattern) =>
        "/" + string.Join("/", pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart => "v1",
            _ => string.Empty,
        }))));

    private static string Describe(RouteEndpoint e) => $"{e.DisplayName} ({e.RoutePattern.RawText})";
}
