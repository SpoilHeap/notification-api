using System.Diagnostics.CodeAnalysis;

namespace NotificationApi.Domain.Notifications;

/// <summary>
/// Identifies a <see cref="Notification"/>. On the wire it is <c>ntf_</c> plus the 32 lower-case hex digits of a
/// time-ordered UUID (version 7), e.g. <c>ntf_019ac63f8e407c3a9d1b2e4f5a6b7c8d</c> (spec §4.1).
/// </summary>
public readonly record struct NotificationId(Guid Value)
{
    public const string Prefix = "ntf_";

    public static NotificationId New(DateTimeOffset now) => new(Guid.CreateVersion7(now));

    public static bool TryParse([NotNullWhen(true)] string? value, out NotificationId id)
    {
        id = default;
        if (!TypedIds.TryParse(Prefix, value, out var guid))
        {
            return false;
        }

        id = new NotificationId(guid);
        return true;
    }

    public override string ToString() => Prefix + Value.ToString("N");
}

/// <summary>
/// Identifies a <see cref="Delivery"/>: <c>dlv_</c> plus the 32 lower-case hex digits of a UUID version 7
/// (spec §4.1).
/// </summary>
public readonly record struct DeliveryId(Guid Value)
{
    public const string Prefix = "dlv_";

    public static DeliveryId New(DateTimeOffset now) => new(Guid.CreateVersion7(now));

    public static bool TryParse([NotNullWhen(true)] string? value, out DeliveryId id)
    {
        id = default;
        if (!TypedIds.TryParse(Prefix, value, out var guid))
        {
            return false;
        }

        id = new DeliveryId(guid);
        return true;
    }

    public override string ToString() => Prefix + Value.ToString("N");
}

internal static class TypedIds
{
    private static readonly System.Buffers.SearchValues<char> LowerHex = System.Buffers.SearchValues.Create("0123456789abcdef");

    public static bool TryParse(string prefix, string? value, out Guid guid)
    {
        guid = default;
        return value is not null
            && value.Length == prefix.Length + 32
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && !value.AsSpan(prefix.Length).ContainsAnyExcept(LowerHex)
            && Guid.TryParseExact(value.AsSpan(prefix.Length), "N", out guid);
    }
}
