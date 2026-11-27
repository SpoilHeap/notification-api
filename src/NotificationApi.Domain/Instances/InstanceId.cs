using System.Diagnostics.CodeAnalysis;

namespace NotificationApi.Domain.Instances;

/// <summary>
/// Identifies this add-on installation: a random (version 4) UUID, written as lower-case text (spec §4.2).
/// It is created with the database and never changes, so the HA integration can tell a reinstall from a new token.
/// </summary>
public readonly record struct InstanceId
{
    private InstanceId(Guid value) => Value = value;

    public Guid Value { get; }

    public static InstanceId New() => new(Guid.NewGuid());

    /// <summary>Accepts only a version 4, RFC 9562 variant UUID.</summary>
    public static InstanceId From(Guid value) =>
        IsRandomUuid(value)
            ? new InstanceId(value)
            : throw new ArgumentException("An instance id is a version 4 (random) UUID.", nameof(value));

    public static bool TryParse([NotNullWhen(true)] string? value, out InstanceId id)
    {
        id = default;
        if (!Guid.TryParseExact(value, "D", out var guid) || !IsRandomUuid(guid))
        {
            return false;
        }

        id = new InstanceId(guid);
        return true;
    }

    public static InstanceId Parse(string value) =>
        TryParse(value, out var id) ? id : throw new FormatException($"'{value}' is not a version 4 UUID.");

    /// <summary>Lower-case, hyphenated: <c>8d3f0c3e-5b1a-4c2e-9f00-1a2b3c4d5e6f</c>.</summary>
    public override string ToString() => Value.ToString("D");

    // The variant is the top bits of byte 8 in RFC order; Guid.Variant reports them directly (0b10xx => 8..11).
    private static bool IsRandomUuid(Guid value) => value.Version == 4 && value.Variant is >= 8 and <= 11;
}
