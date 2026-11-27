using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using NotificationApi.Application.Common;
using NotificationApi.Application.Instances;
using NotificationApi.Application.Recipients;
using NotificationApi.Application.Webhooks;
using NotificationApi.Domain.ApiClients;
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

        api.MapGet("/ping", PingAsync)
            .WithName("Ping")
            .WithSummary("Checks the API token and identifies the installation. The HA integration calls this during setup.");

        api.MapGet("/recipients", GetRecipientsAsync)
            .WithName("GetRecipients")
            .WithSummary("Every contact and group, with no PII. Send If-None-Match with the last ETag to get 304 when nothing changed.")
            .Produces(StatusCodes.Status304NotModified);

        api.MapPut("/webhook", RegisterWebhookAsync)
            .WithName("RegisterWebhook")
            .WithSummary("Registers the webhook that events are POSTed to, replacing any earlier one.");
        api.MapGet("/webhook", GetWebhookAsync)
            .WithName("GetWebhook")
            .WithSummary("The registered webhook URL (never the secret). 404 when none is registered.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        api.MapDelete("/webhook", RemoveWebhookAsync)
            .WithName("RemoveWebhook")
            .WithSummary("Removes the webhook, which stops events. Succeeds when none is registered.");

        return app;
    }

    private static async Task<Ok<PingResponse>> PingAsync(EnsureInstance ensureInstance, CancellationToken cancellationToken)
    {
        var instance = await ensureInstance.HandleAsync(cancellationToken);
        return TypedResults.Ok(new PingResponse(Ok: true, Version, instance.Id.ToString()));
    }

    private static async Task<Results<Ok<RecipientsResponse>, StatusCodeHttpResult>> GetRecipientsAsync(
        HttpContext context, GetRecipients getRecipients, CancellationToken cancellationToken)
    {
        var list = await getRecipients.HandleAsync(cancellationToken);
        context.Response.Headers.ETag = RecipientsETag.For(list.Revision);

        if (RecipientsETag.IsNotModified(context.Request.Headers.IfNoneMatch, list.Revision))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        return TypedResults.Ok(new RecipientsResponse(
            list.Revision,
            [.. list.Recipients.Select(r => new RecipientResponse(r.Handle, r.Name, r.Type))]));
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> RegisterWebhookAsync(
        RegisterWebhookRequest request, ClaimsPrincipal user, RegisterWebhook registerWebhook, CancellationToken cancellationToken)
    {
        var result = await registerWebhook.HandleAsync(
            ClientId(user), new RegisterWebhookCommand(request.Url, request.Secret), cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.NoContent();
        }

        return result.Error.Code == Error.ValidationCode
            ? TypedResults.ValidationProblem(result.Error.FieldErrors, detail: result.Error.Message)
            : TypedResults.Problem(result.Error.Message, statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<Results<Ok<WebhookResponse>, ProblemHttpResult>> GetWebhookAsync(
        ClaimsPrincipal user, GetWebhook getWebhook, CancellationToken cancellationToken)
    {
        var registration = await getWebhook.HandleAsync(ClientId(user), cancellationToken);
        return registration is null
            ? TypedResults.Problem("No webhook is registered.", statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(new WebhookResponse(registration.Url.OriginalString, registration.RegisteredAt.UtcDateTime));
    }

    private static async Task<NoContent> RemoveWebhookAsync(ClaimsPrincipal user, RemoveWebhook removeWebhook, CancellationToken cancellationToken)
    {
        await removeWebhook.HandleAsync(ClientId(user), cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>The API client the bearer token belongs to (set by the <c>ApiToken</c> scheme).</summary>
    private static ApiClientId ClientId(ClaimsPrincipal user) =>
        new(Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("An authenticated API caller has no client id.")));
}

/// <param name="Ok">Always true: a valid token reached the API.</param>
/// <param name="Version">The add-on version, matching <c>config.yaml</c> in released images.</param>
/// <param name="InstanceId">A lower-case UUID created with the database and never changed (spec §4.2, AC44).</param>
internal sealed record PingResponse(bool Ok, string Version, string InstanceId);

/// <param name="Revision">Opaque. Changes whenever a contact or group is created, renamed or deleted. Also sent as the ETag.</param>
/// <param name="Recipients">Every contact and group.</param>
internal sealed record RecipientsResponse(string Revision, IReadOnlyList<RecipientResponse> Recipients);

/// <param name="Handle">E.g. <c>group:family</c>. Never changes.</param>
/// <param name="Name">The display name.</param>
/// <param name="Type"><c>contact</c> or <c>group</c>.</param>
internal sealed record RecipientResponse(string Handle, string Name, string Type);

/// <param name="Url">Absolute http(s) URL that events are POSTed to.</param>
/// <param name="Secret">Standard base64 of at least 32 random bytes. The HMAC key is the decoded bytes.</param>
internal sealed record RegisterWebhookRequest(string? Url, string? Secret)
{
    public override string ToString() => $"RegisterWebhookRequest {{ Url = {Url}, Secret = (redacted) }}";
}

/// <param name="Url">The registered URL.</param>
/// <param name="RegisteredAt">When it was registered (UTC).</param>
internal sealed record WebhookResponse(string Url, DateTime RegisteredAt);
