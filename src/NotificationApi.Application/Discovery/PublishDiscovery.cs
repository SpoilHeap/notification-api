using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationApi.Application.ApiAccess;

namespace NotificationApi.Application.Discovery;

/// <summary>
/// Makes sure the API token exists, then announces the add-on to HA through Supervisor discovery (spec §9.4).
/// Runs on every start with the same token: the Supervisor de-duplicates identical messages, and the
/// integration's stored token stays valid. Transient failures are retried with exponential backoff.
/// </summary>
public sealed partial class PublishDiscovery(
    EnsureApiToken ensureApiToken,
    IDiscoveryPublisher publisher,
    DiscoveryStatusStore status,
    IOptions<DiscoveryOptions> options,
    TimeProvider time,
    ILogger<PublishDiscovery> logger)
{
    /// <summary>The discovery service name, listed under <c>discovery:</c> in config.yaml and the integration's manifest.</summary>
    public const string ServiceName = "notification_api";

    public async Task<DiscoveryStatus> HandleAsync(CancellationToken cancellationToken)
    {
        // The token is provisioned even when discovery is skipped, so the manual fallback can show it.
        var token = await ensureApiToken.HandleAsync(cancellationToken);
        if (token.IsFailure)
        {
            LogNoToken(logger, token.Error.Code);
            return Record(DiscoveryStatus.Failed(token.Error.Message, 0, time.GetUtcNow()));
        }

        if (!publisher.IsConfigured)
        {
            LogSkipped(logger);
            return Record(DiscoveryStatus.Skipped("SUPERVISOR_TOKEN is not set: not running as an HA add-on.", time.GetUtcNow()));
        }

        var settings = options.Value;
        var message = new DiscoveryMessage(ServiceName, new DiscoveryConfig(settings.Host, settings.Port, token.Value));
        var delay = settings.InitialRetryDelay;

        for (var attempt = 1; ; attempt++)
        {
            var result = await publisher.PublishAsync(message, cancellationToken);
            if (result.IsSuccess)
            {
                LogPublished(logger, result.Value.Uuid, settings.Host, settings.Port, attempt);
                return Record(DiscoveryStatus.Published(result.Value.Uuid, attempt, time.GetUtcNow()));
            }

            var error = result.Error;
            var retry = DiscoveryErrors.IsTransient(error) && attempt < settings.MaxAttempts;
            if (!retry)
            {
                LogFailed(logger, attempt, error.Code, error.Message);
                return Record(DiscoveryStatus.Failed(error.Message, attempt, time.GetUtcNow()));
            }

            LogRetrying(logger, attempt, settings.MaxAttempts, error.Code, error.Message, delay);
            Record(DiscoveryStatus.Retrying(error.Message, attempt, time.GetUtcNow()));
            await Task.Delay(delay, time, cancellationToken);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, settings.MaxRetryDelay.Ticks));
        }
    }

    private DiscoveryStatus Record(DiscoveryStatus value)
    {
        status.Set(value);
        return value;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Supervisor discovery skipped: SUPERVISOR_TOKEN is not set (not running as an HA add-on).")]
    private static partial void LogSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published Supervisor discovery {Uuid} for {Host}:{Port} (attempt {Attempt}).")]
    private static partial void LogPublished(ILogger logger, string uuid, string host, int port, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Supervisor discovery attempt {Attempt}/{MaxAttempts} failed ({Code}: {Detail}). Retrying in {Delay}.")]
    private static partial void LogRetrying(ILogger logger, int attempt, int maxAttempts, string code, string detail, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Supervisor discovery failed after {Attempts} attempt(s) ({Code}: {Detail}). Set up the integration manually from the Integration page.")]
    private static partial void LogFailed(ILogger logger, int attempts, string code, string detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Supervisor discovery not attempted: no usable API token ({Code}).")]
    private static partial void LogNoToken(ILogger logger, string code);
}
