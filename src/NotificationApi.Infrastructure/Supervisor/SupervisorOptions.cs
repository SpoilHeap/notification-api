namespace NotificationApi.Infrastructure.Supervisor;

/// <summary>
/// HA Supervisor API settings (config section <c>Supervisor</c>). The token defaults to the
/// <c>SUPERVISOR_TOKEN</c> environment variable that the Supervisor sets in every add-on with <c>hassio_api: true</c>.
/// </summary>
public sealed class SupervisorOptions
{
    public const string SectionName = "Supervisor";

    /// <summary>The environment variable the Supervisor injects.</summary>
    public const string TokenEnvironmentVariable = "SUPERVISOR_TOKEN";

    public Uri BaseUrl { get; set; } = new("http://supervisor/");

    /// <summary>Bearer token for the Supervisor API. Empty outside HA OS, which disables discovery.</summary>
    public string? Token { get; set; }

    /// <summary>Per-request timeout. Retries are handled by the discovery use case, not the HTTP pipeline.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
