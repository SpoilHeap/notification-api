using System.Reflection;
using NotificationApi.Web.Authentication;

namespace NotificationApi.Web.Api;

/// <summary><c>/api/v1</c>, used by the HA integration (spec §4). Every endpoint requires the API token.</summary>
internal static class ApiEndpoints
{
    public const string Prefix = "/api/v1";

    /// <summary>The add-on version (the release build passes it as <c>-p:Version</c>), without SourceLink's <c>+&lt;sha&gt;</c> suffix.</summary>
    internal static readonly string Version = AddOnVersion(
        typeof(ApiEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static string AddOnVersion(string? informationalVersion) =>
        string.IsNullOrWhiteSpace(informationalVersion) ? "unknown" : informationalVersion.Split('+', 2)[0];

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
/// <param name="Version">The add-on version, matching <c>config.yaml</c> in released images.</param>
internal sealed record PingResponse(bool Ok, string Version);
