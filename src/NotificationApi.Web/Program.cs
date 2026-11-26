using Microsoft.AspNetCore.DataProtection;
using NotificationApi.Application;
using NotificationApi.Infrastructure;
using NotificationApi.Web.Components;
using NotificationApi.Web.Hosting;
using NotificationApi.Web.Ingress;

if (HealthProbe.IsRequested(args))
{
    return await HealthProbe.RunAsync(); // Docker HEALTHCHECK mode: probe /health and exit.
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole(o => o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ");

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddIngress(builder.Configuration, builder.Environment);

// Data Protection keys live in /data so encrypted secrets and antiforgery tokens survive restarts and updates.
var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
builder.Services.AddDataProtection()
    .SetApplicationName("NotificationApi")
    .PersistKeysToFileSystem(Directory.CreateDirectory(storage.KeysPath));

builder.Services.AddRazorComponents(); // Static SSR only: no interactive render modes (ADR 0004).
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseIngress(); // Must run before routing so PathBase applies to endpoint matching and link generation.
app.UseRouting();
app.UseAntiforgery();

app.MapHealthChecks("/health");
app.MapOpenApi();
app.MapStaticAssets();
app.MapRazorComponents<App>();

await app.RunAsync();
return 0;

/// <summary>Entry point, exposed for <c>WebApplicationFactory</c> integration tests.</summary>
public partial class Program;
