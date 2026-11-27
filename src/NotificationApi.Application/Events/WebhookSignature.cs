using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NotificationApi.Application.Events;

/// <summary>
/// The webhook signature (spec §4.4, AC33, AC37): HMAC-SHA256, keyed with the decoded secret bytes, over the ASCII bytes
/// of <c>"&lt;timestamp&gt;."</c> followed by the exact raw body bytes sent.
/// </summary>
public static class WebhookSignature
{
    public const string TimestampHeader = "X-Notification-Api-Timestamp";

    public const string SignatureHeader = "X-Notification-Api-Signature";

    public const string Prefix = "sha256=";

    /// <returns><c>sha256=</c> plus 64 lower-case hex digits.</returns>
    public static string Compute(ReadOnlySpan<byte> secret, long unixSeconds, ReadOnlySpan<byte> body)
    {
        var timestamp = Encoding.ASCII.GetBytes(FormatTimestamp(unixSeconds) + ".");
        var message = new byte[timestamp.Length + body.Length];
        timestamp.CopyTo(message, 0);
        body.CopyTo(message.AsSpan(timestamp.Length));

        return Prefix + Convert.ToHexStringLower(HMACSHA256.HashData(secret, message));
    }

    /// <summary>Integer Unix seconds as a decimal string, the value of <see cref="TimestampHeader"/>.</summary>
    public static string FormatTimestamp(long unixSeconds) => unixSeconds.ToString(CultureInfo.InvariantCulture);
}
