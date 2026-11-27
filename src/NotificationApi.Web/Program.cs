using System.Text.Json;
using NotificationApi.Application;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Instances;
using NotificationApi.Infrastructure;
using NotificationApi.Infrastructure.Persistence;
using NotificationApi.Web.Api;
using NotificationApi.Web.Authentication;
using NotificationApi.Web.Components;
using NotificationApi.Web.Hosting;
using NotificationApi.Web.Ingress;

if (HealthProbe.IsRequested(args))
{
    return await HealthProbe.RunAsync(); // Docker HEALTHCHECK mode: probe /health and exit.
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole(o => o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ");

// Infrastructure also sets up Data Protection, with keys in <data path>/keys.
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddIngress(builder.Configuration, builder.Environment)
    .AddNotificationApiAuth()
    .AddDiscovery(builder.Configuration);

// The API contract is snake_case throughout (spec §4): instance_id, registered_at, idempotency_key.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
builder.Services.AddProblemDetails();
builder.Services.AddRazorComponents(); // Static SSR only: no interactive render modes (ADR 0004).
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Notification API";
    document.Info.Description = "Used by the Home Assistant integration. Authenticate with Authorization: Bearer <api token>.";
    return Task.CompletedTask;
}));
builder.Services.AddHealthChecks();

var app = builder.Build();

await app.Services.MigrateDatabaseAsync();
await using (var scope = app.Services.CreateAsyncScope())
{
    // Provision the API token before serving, so the UI and discovery never race to create it.
    // A failure (unreadable token) is reported by discovery and on the Integration page.
    await scope.ServiceProvider.GetRequiredService<EnsureApiToken>().HandleAsync(CancellationToken.None);
    // Created once with the database, then never changed (spec §4.2, AC44).
    await scope.ServiceProvider.GetRequiredService<EnsureInstance>().HandleAsync(CancellationToken.None);
}

app.UseIngress(); // Must run before routing so PathBase applies to endpoint matching and link generation.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization(); // Fallback policy = deny: every endpoint below opts in explicitly.
app.UseAntiforgery();

// Anonymous: the Docker HEALTHCHECK (--healthcheck) probes it from inside the container.
app.MapHealthChecks("/health").AllowAnonymous();
app.MapApiV1();
// Contract for the HA integration. Readable through Ingress only, so other add-ons can't enumerate the API.
app.MapOpenApi().RequireAuthorization(AuthPolicies.Ingress);
app.MapStaticAssets().RequireAuthorization(AuthPolicies.Ingress);
app.MapRazorComponents<App>().RequireAuthorization(AuthPolicies.Ingress);

await app.RunAsync();
return 0;

/// <summary>Entry point, exposed for <c>WebApplicationFactory</c> integration tests.</summary>
public partial class Program;
