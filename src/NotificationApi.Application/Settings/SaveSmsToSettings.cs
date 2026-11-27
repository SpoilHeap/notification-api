using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Domain.Audit;
using NotificationApi.Domain.Settings;

namespace NotificationApi.Application.Settings;

/// <summary>
/// Saves the SMS.to API key and sender ID from the Settings page (AC70). The key is encrypted before it is stored and
/// is never logged, audited or shown again (only masked). A blank key keeps the stored one. The change is audited
/// (AC82). The SMS.to channel reads the settings at send time, so a change applies without a restart.
/// </summary>
public sealed partial class SaveSmsToSettings(
    ISettingsRepository settings,
    ISecretProtector protector,
    IUnitOfWork unitOfWork,
    AuditTrail audit,
    TimeProvider time,
    ILogger<SaveSmsToSettings> logger)
{
    public const string EntityKey = "sms_to";

    public async Task<Result<SmsToSettingsView>> HandleAsync(AuditActor actor, SaveSmsToSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(command);
        var current = await settings.GetAsync(cancellationToken);
        var errors = SmsToApiKey.Validate(command, current?.SmsToConfigured == true, out var apiKey, out var senderId);
        if (errors.Count > 0)
        {
            var fields = string.Join(", ", errors.Keys);
            LogRejected(logger, fields);
            return Error.Validation(errors);
        }

        var now = time.GetUtcNow();
        var row = current ?? AppSettings.CreateDefault(now);
        var wasConfigured = row.SmsToConfigured;
        var previousSender = row.SmsToSenderId;

        var changes = new List<string>();
        if (apiKey is not null)
        {
            changes.Add(wasConfigured ? "API key replaced" : "API key set");
        }

        if (senderId != previousSender)
        {
            changes.Add(senderId is null ? "sender ID cleared (account default)" : $"sender ID set to '{senderId}'");
        }

        if (changes.Count == 0)
        {
            return SmsToSettingsView.From(row);
        }

        row.SetSmsTo(apiKey is null ? null : protector.Protect(apiKey), apiKey is null ? null : SmsToApiKey.Hint(apiKey), senderId, now);
        if (current is null)
        {
            settings.Add(row);
        }

        audit.Record(actor, wasConfigured ? AuditAction.Update : AuditAction.Create, AuditEntityType.Settings, EntityKey, "SMS.to: " + string.Join(", ", changes));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogSaved(logger, actor.HaUserId, apiKey is not null);
        return SmsToSettingsView.From(row);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "HA user '{HaUserId}' saved the SMS.to settings (new API key: {KeyChanged}).")]
    private static partial void LogSaved(ILogger logger, string haUserId, bool keyChanged);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected the SMS.to settings: invalid {Fields}.")]
    private static partial void LogRejected(ILogger logger, string fields);
}
