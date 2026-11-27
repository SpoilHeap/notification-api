using System.Diagnostics.CodeAnalysis;

namespace NotificationApi.Application.Webhooks;

/// <summary>
/// The shared webhook secret as the integration sends it: standard base64 (with padding, no whitespace) of at least
/// <see cref="MinBytes"/> random bytes (spec §4.3). The HMAC key is the decoded bytes, not the base64 text.
/// </summary>
public static class WebhookSecret
{
    public const int MinBytes = 32;

    /// <summary>An upper bound, so a huge value can't be stored. Far above any sensible key.</summary>
    public const int MaxBytes = 512;

    public static bool TryDecode([NotNullWhen(true)] string? base64, [NotNullWhen(true)] out byte[]? key)
    {
        key = null;
        if (string.IsNullOrEmpty(base64) || base64.Length > (MaxBytes + 2) / 3 * 4 || base64.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var buffer = new byte[base64.Length / 4 * 3];
        if (!Convert.TryFromBase64String(base64, buffer, out var written) || written is < MinBytes or > MaxBytes)
        {
            return false;
        }

        key = buffer[..written];
        return true;
    }
}
