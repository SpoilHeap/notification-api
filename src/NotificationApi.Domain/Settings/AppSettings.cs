namespace NotificationApi.Domain.Settings;

/// <summary>
/// Runtime settings edited on the web UI's Settings page (spec §7 "Settings"): one row. Provider secrets are held
/// only in protected (encrypted) form; this type never sees the plaintext (ADR 0012).
/// </summary>
public sealed class AppSettings
{
    /// <summary>The single row's key.</summary>
    public const int SingletonId = 1;

    /// <summary>SMS.to accepts alphanumeric sender IDs of up to 11 characters, or numeric ones of up to 15.</summary>
    public const int MaxSmsToSenderIdLength = 15;

    private AppSettings(int id, DateTimeOffset updatedAt)
    {
        Id = id;
        UpdatedAt = updatedAt;
    }

    public int Id { get; private set; }

    /// <summary>The SMS.to API key, encrypted with Data Protection. Null when SMS.to isn't configured.</summary>
    public string? SmsToProtectedApiKey { get; private set; }

    /// <summary>A hint for the UI (the key's last characters), so the full key never has to be decrypted to show it.</summary>
    public string? SmsToApiKeyHint { get; private set; }

    /// <summary>The sender shown on the phone. Null means the SMS.to account's default sender.</summary>
    public string? SmsToSenderId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool SmsToConfigured => SmsToProtectedApiKey is not null;

    public static AppSettings CreateDefault(DateTimeOffset now) => new(SingletonId, now);

    /// <summary>Sets the SMS.to credentials. A null <paramref name="protectedApiKey"/> keeps the stored key.</summary>
    public void SetSmsTo(string? protectedApiKey, string? apiKeyHint, string? senderId, DateTimeOffset now)
    {
        if (protectedApiKey is null && SmsToProtectedApiKey is null)
        {
            throw new InvalidOperationException("SMS.to needs an API key.");
        }

        if (senderId is { Length: > MaxSmsToSenderIdLength })
        {
            throw new ArgumentException($"At most {MaxSmsToSenderIdLength} characters.", nameof(senderId));
        }

        if (protectedApiKey is not null)
        {
            SmsToProtectedApiKey = protectedApiKey;
            SmsToApiKeyHint = apiKeyHint;
        }

        SmsToSenderId = string.IsNullOrWhiteSpace(senderId) ? null : senderId;
        UpdatedAt = now;
    }

    /// <summary>Removes the SMS.to credentials. Returns false when none were set.</summary>
    public bool ClearSmsTo(DateTimeOffset now)
    {
        if (SmsToProtectedApiKey is null && SmsToSenderId is null)
        {
            return false;
        }

        SmsToProtectedApiKey = null;
        SmsToApiKeyHint = null;
        SmsToSenderId = null;
        UpdatedAt = now;
        return true;
    }
}
