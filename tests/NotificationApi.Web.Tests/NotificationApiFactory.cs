using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NotificationApi.Web.Tests;

/// <summary>
/// Runs the app in the Production environment with a temporary data path.
/// Send <see cref="RemoteIpHeader"/> to simulate the client address (TestServer has none).
/// Every log message is captured in <see cref="Logs"/>.
/// </summary>
public sealed class NotificationApiFactory : WebApplicationFactory<Program>
{
    public const string RemoteIpHeader = "X-Test-Remote-Ip";
    public const string IngressProxyIp = "172.30.32.2";
    public const string OtherIp = "192.168.1.50";
    public const string HaUserId = "8f7e6d5c4b3a";

    /// <summary>Set to share a data path between factories (e.g. to simulate a restart). The caller then deletes it.</summary>
    public string DataPath { get; init; } = Path.Combine(Path.GetTempPath(), "notification-api-tests", Guid.NewGuid().ToString("N"));

    public bool DeleteDataOnDispose { get; init; } = true;

    public LogSink Logs { get; } = new();

    /// <summary>A request as HA Ingress sends it: from the proxy address, with the identity headers.</summary>
    public static HttpRequestMessage IngressRequest(HttpMethod method, string path, string remoteIp = IngressProxyIp, bool withUser = true)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(RemoteIpHeader, remoteIp);
        if (withUser)
        {
            request.Headers.Add("X-Remote-User-Id", HaUserId);
            request.Headers.Add("X-Remote-User-Name", "alice");
            request.Headers.Add("X-Remote-User-Display-Name", "Alice");
        }

        return request;
    }

    /// <summary>The plaintext API token, as the HA integration receives it through discovery.</summary>
    public Task<string> GetApiTokenAsync() => Services.GetApiTokenAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Storage:DataPath", DataPath);
        builder.UseSetting("Discovery:Host", "notification-api-test");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (DeleteDataOnDispose && Directory.Exists(DataPath))
        {
            Directory.Delete(DataPath, recursive: true);
        }
    }

    /// <summary>Runs before the app's own pipeline, so the Ingress middleware and handler see the fake address.</summary>
    private sealed class FakeRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[RemoteIpHeader], out var ip))
                {
                    context.Connection.RemoteIpAddress = ip;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

/// <summary>Captures every log message with its structured values, to check that secrets never reach the logs (AC74).</summary>
public sealed class LogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new SinkLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class SinkLogger(LogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            sink._entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} [{values}] {exception}");
        }
    }
}
