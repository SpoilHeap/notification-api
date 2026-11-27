using Microsoft.Extensions.Logging;
using NotificationApi.Application.Common;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.ApiAccess;

/// <summary>Shows the API token in the web UI for manual integration setup, the fallback when discovery fails (spec §9.4).</summary>
public sealed partial class RevealApiToken(EnsureApiToken ensureApiToken, ILogger<RevealApiToken> logger)
{
    /// <param name="haUserId">The HA user asking, from the Ingress identity headers.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public async Task<Result<ApiToken>> HandleAsync(string haUserId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(haUserId);

        var token = await ensureApiToken.HandleAsync(cancellationToken);
        if (token.IsSuccess)
        {
            // TODO(AuditLog, spec AC82): record this in the audit log once it exists.
            LogRevealed(logger, haUserId);
        }

        return token;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The API token was revealed in the web UI to HA user '{HaUserId}'.")]
    private static partial void LogRevealed(ILogger logger, string haUserId);
}
