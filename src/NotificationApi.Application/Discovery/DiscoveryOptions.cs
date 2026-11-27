using Microsoft.Extensions.Options;

namespace NotificationApi.Application.Discovery;

/// <summary>Supervisor discovery settings (config section <c>Discovery</c>).</summary>
public sealed class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    /// <summary>The hostname HA uses to reach the add-on. The Web host defaults it to the container hostname.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The API port (the add-on's <c>ingress_port</c>, spec §4).</summary>
    public int Port { get; set; } = 8080;

    /// <summary>Total attempts, including the first.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Delay before the first retry. It doubles for each retry, up to <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Hand-written validator, checked on start.</summary>
public sealed class DiscoveryOptionsValidator : IValidateOptions<DiscoveryOptions>
{
    public ValidateOptionsResult Validate(string? name, DiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (Uri.CheckHostName(options.Host) == UriHostNameType.Unknown)
        {
            failures.Add("Discovery:Host must be a hostname or IP address.");
        }

        if (options.Port is < 1 or > 65535)
        {
            failures.Add("Discovery:Port must be between 1 and 65535.");
        }

        if (options.MaxAttempts is < 1 or > 20)
        {
            failures.Add("Discovery:MaxAttempts must be between 1 and 20.");
        }

        if (options.InitialRetryDelay <= TimeSpan.Zero || options.InitialRetryDelay > options.MaxRetryDelay)
        {
            failures.Add("Discovery:InitialRetryDelay must be positive and no greater than Discovery:MaxRetryDelay.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
