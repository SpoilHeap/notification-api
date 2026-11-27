using NotificationApi.Application.Common;

namespace NotificationApi.Application.Settings;

/// <summary>
/// The SMS.to credentials, decrypted, for the SMS.to channel at send time. Read on every send, so a change on the
/// Settings page applies to the next attempt without a restart.
/// </summary>
public sealed class ReadSmsToCredentials(ISettingsRepository settings, ISecretProtector protector)
{
    public async Task<Result<SmsToCredentials>> HandleAsync(CancellationToken cancellationToken)
    {
        var row = await settings.GetAsync(cancellationToken);
        if (row?.SmsToProtectedApiKey is not { } protectedKey)
        {
            return SmsToErrors.NotConfigured;
        }

        var key = protector.Unprotect(protectedKey);
        return key.IsSuccess ? new SmsToCredentials(key.Value, row.SmsToSenderId) : SmsToErrors.UnreadableKey;
    }
}
