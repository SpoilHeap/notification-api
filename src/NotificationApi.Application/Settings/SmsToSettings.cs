using System.Text.RegularExpressions;
using NotificationApi.Application.Common;
using NotificationApi.Domain.Settings;

namespace NotificationApi.Application.Settings;

/// <summary>What the Settings page shows for SMS.to. The API key is masked and never decrypted for display (AC70).</summary>
/// <param name="Configured">Whether an API key is stored.</param>
/// <param name="MaskedApiKey">E.g. <c>••••••••x7Qz</c>, or null when not configured.</param>
/// <param name="SenderId">The sender ID, or null for the account's default.</param>
public sealed record SmsToSettingsView(bool Configured, string? MaskedApiKey, string? SenderId)
{
    public static SmsToSettingsView From(AppSettings? settings) =>
        settings is { SmsToConfigured: true }
            ? new SmsToSettingsView(true, SmsToApiKey.Mask(settings.SmsToApiKeyHint), settings.SmsToSenderId)
            : new SmsToSettingsView(false, null, settings?.SmsToSenderId);
}

/// <param name="ApiKey">A new key, or blank to keep the stored one.</param>
/// <param name="SenderId">The sender ID, or blank for the account's default.</param>
public sealed record SaveSmsToSettingsCommand(string? ApiKey, string? SenderId)
{
    public override string ToString() => $"SaveSmsToSettingsCommand {{ ApiKey = (redacted), SenderId = {SenderId} }}";
}

/// <summary>The decrypted credentials, for the SMS.to channel at send time only. <see cref="ToString"/> redacts the key.</summary>
public sealed record SmsToCredentials(string ApiKey, string? SenderId)
{
    public override string ToString() => $"SmsToCredentials {{ ApiKey = (redacted), SenderId = {SenderId} }}";
}

public static class SmsToFields
{
    public const string ApiKey = "api_key";
    public const string SenderId = "sender_id";
}

public static class SmsToErrors
{
    public static readonly Error NotConfigured = new("sms_to.not_configured", "SMS.to is not configured");

    public static readonly Error UnreadableKey = new(
        "sms_to.key_unreadable",
        "The SMS.to API key can't be decrypted (the Data Protection keys in /data/keys may be missing or replaced). Enter it again on the Settings page");
}

internal static partial class SmsToApiKey
{
    public const int MaxLength = 512;

    private const int HintLength = 4;
    private const int MinLengthForHint = 12;

    /// <summary>The last few characters, only when the key is long enough that they give little away.</summary>
    public static string? Hint(string apiKey) => apiKey.Length >= MinLengthForHint ? apiKey[^HintLength..] : null;

    public static string Mask(string? hint) => "••••••••" + (hint ?? string.Empty);

    public static IReadOnlyDictionary<string, string[]> Validate(SaveSmsToSettingsCommand command, bool hasStoredKey, out string? apiKey, out string? senderId)
    {
        var errors = new Dictionary<string, string[]>();
        apiKey = string.IsNullOrWhiteSpace(command.ApiKey) ? null : command.ApiKey.Trim();
        senderId = string.IsNullOrWhiteSpace(command.SenderId) ? null : command.SenderId.Trim();

        if (apiKey is null && !hasStoredKey)
        {
            errors[SmsToFields.ApiKey] = ["The API key is required."];
        }
        else if (apiKey is not null && (apiKey.Length > MaxLength || apiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))))
        {
            errors[SmsToFields.ApiKey] = [$"The API key must be at most {MaxLength} characters, with no spaces."];
        }

        if (senderId is not null && !SenderIdPattern().IsMatch(senderId))
        {
            errors[SmsToFields.SenderId] = ["Use up to 11 letters, digits or spaces, or a number of up to 15 digits (optionally starting with +)."];
        }

        return errors;
    }

    [GeneratedRegex(@"^(?:[A-Za-z0-9 ]{1,11}|\+?[0-9]{1,15})$", RegexOptions.CultureInvariant)]
    private static partial Regex SenderIdPattern();
}
