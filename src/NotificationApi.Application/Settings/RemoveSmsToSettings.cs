using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Settings;

/// <summary>Removes the SMS.to credentials. SMS deliveries then fail with "SMS.to is not configured". Audited (AC82).</summary>
public sealed partial class RemoveSmsToSettings(
    ISettingsRepository settings,
    IUnitOfWork unitOfWork,
    AuditTrail audit,
    TimeProvider time,
    ILogger<RemoveSmsToSettings> logger)
{
    /// <returns>False when nothing was configured.</returns>
    public async Task<bool> HandleAsync(AuditActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var row = await settings.GetAsync(cancellationToken);
        if (row is null || !row.ClearSmsTo(time.GetUtcNow()))
        {
            return false;
        }

        audit.Record(actor, AuditAction.Delete, AuditEntityType.Settings, SaveSmsToSettings.EntityKey, "SMS.to: API key and sender ID removed");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogRemoved(logger, actor.HaUserId);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "HA user '{HaUserId}' removed the SMS.to settings.")]
    private static partial void LogRemoved(ILogger logger, string haUserId);
}
