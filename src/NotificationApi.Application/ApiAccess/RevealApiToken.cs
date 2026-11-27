using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Domain.ApiClients;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.ApiAccess;

/// <summary>
/// Shows the API token in the web UI for manual integration setup, the fallback when discovery fails (spec §9.4).
/// Every reveal is written to the audit log (AC82); the entry never contains the token.
/// </summary>
public sealed partial class RevealApiToken(
    EnsureApiToken ensureApiToken,
    AuditTrail audit,
    IUnitOfWork unitOfWork,
    ILogger<RevealApiToken> logger)
{
    /// <param name="actor">The HA user asking, from the Ingress identity headers.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public async Task<Result<ApiToken>> HandleAsync(AuditActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var token = await ensureApiToken.HandleAsync(cancellationToken);
        if (token.IsSuccess)
        {
            audit.Record(actor, AuditAction.Reveal, AuditEntityType.ApiToken, ApiClientNames.HomeAssistant, "API token revealed on the Integration page");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            LogRevealed(logger, actor.HaUserId);
        }

        return token;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The API token was revealed in the web UI to HA user '{HaUserId}'.")]
    private static partial void LogRevealed(ILogger logger, string haUserId);
}
