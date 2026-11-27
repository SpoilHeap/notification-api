using System.Reflection;
using NotificationApi.Web.Authentication;

namespace NotificationApi.Web.Api;

/// <summary><c>/api/v1</c>, used by the HA integration (spec §4). Every endpoint requires the API token.</summary>
internal static class ApiEndpoints
{
    public const string Prefix = "/api/v1";

    private static readonly string Version =
        typeof(ApiEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static IEndpointRouteBuilder MapApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(Prefix)
            .RequireAuthorization(AuthPolicies.ApiToken)
            .WithTags("v1")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        api.MapGet("/ping", () => TypedResults.Ok(new PingResponse(Ok: true, Version)))
            .WithName("Ping")
            .WithSummary("Checks the API token. The HA integration calls this during setup.");

        return app;
    }
}

/// <param name="Ok">Always true: a valid token reached the API.</param>
/// <param name="Version">The add-on's informational version.</param>
internal sealed record PingResponse(bool Ok, string Version);
