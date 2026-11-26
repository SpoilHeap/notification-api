namespace NotificationApi.Web.Hosting;

/// <summary>
/// <c>dotnet NotificationApi.Web.dll --healthcheck</c>: the Docker HEALTHCHECK used by the Supervisor
/// watchdog. The chiseled image has no shell or curl, so the app probes its own /health endpoint
/// and exits 0 (healthy) or 1 (unhealthy).
/// </summary>
internal static class HealthProbe
{
    public const string Argument = "--healthcheck";

    public static bool IsRequested(string[] args) => args is [Argument];

    public static Task<int> RunAsync() =>
        CheckAsync(LocalHealthUri(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")));

    internal static async Task<int> CheckAsync(Uri healthUri, HttpMessageHandler? handler = null)
    {
        using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(3);
        try
        {
            using var response = await client.GetAsync(healthUri);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }

    /// <summary>Uses the first port in <c>ASPNETCORE_HTTP_PORTS</c> (e.g. "8080" or "8080;8081"), default 8080.</summary>
    internal static Uri LocalHealthUri(string? httpPorts)
    {
        var first = httpPorts?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var port = int.TryParse(first, out var p) && p is > 0 and <= 65535 ? p : 8080;
        return new Uri($"http://127.0.0.1:{port}/health");
    }
}
