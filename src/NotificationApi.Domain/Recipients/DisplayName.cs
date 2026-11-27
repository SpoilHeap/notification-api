using System.Diagnostics.CodeAnalysis;

namespace NotificationApi.Domain.Recipients;

/// <summary>The name people see for a contact or group: required, trimmed, at most <see cref="MaxLength"/> characters, one line.</summary>
public sealed record DisplayName
{
    public const int MaxLength = 100;

    private DisplayName(string value) => Value = value;

    public string Value { get; }

    /// <summary>Trims <paramref name="value"/>, then accepts 1 to <see cref="MaxLength"/> characters with no control characters.</summary>
    public static bool TryCreate(string? value, [NotNullWhen(true)] out DisplayName? name)
    {
        name = null;
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength || trimmed.Any(char.IsControl))
        {
            return false;
        }

        name = new DisplayName(trimmed);
        return true;
    }

    public static DisplayName Create(string value) =>
        TryCreate(value, out var name)
            ? name
            : throw new ArgumentException($"A display name is 1-{MaxLength} characters on one line.", nameof(value));

    public override string ToString() => Value;
}
