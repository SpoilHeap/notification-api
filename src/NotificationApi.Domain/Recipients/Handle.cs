using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace NotificationApi.Domain.Recipients;

/// <summary>
/// A stable recipient reference used by Home Assistant, e.g. <c>contact:alice</c> or <c>group:family</c>.
/// Handles never change after creation (spec AC51), so HA automations can rely on them.
/// </summary>
public sealed partial record Handle
{
    public const int MaxNameLength = 64;

    private Handle(HandleType type, string name)
    {
        Type = type;
        Name = name;
    }

    public HandleType Type { get; }

    /// <summary>The name part: lower-case letters, digits and underscores (spec AC50).</summary>
    public string Name { get; }

    public static Handle Contact(string name) => Create(HandleType.Contact, name);

    public static Handle Group(string name) => Create(HandleType.Group, name);

    /// <summary>Parses <c>contact:&lt;name&gt;</c> or <c>group:&lt;name&gt;</c>.</summary>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Handle? handle)
    {
        handle = null;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        HandleType? type = value[..separator] switch
        {
            "contact" => HandleType.Contact,
            "group" => HandleType.Group,
            _ => null,
        };

        var name = value[(separator + 1)..];
        if (type is null || !IsValidName(name))
        {
            return false;
        }

        handle = new Handle(type.Value, name);
        return true;
    }

    public static Handle Parse(string value) =>
        TryParse(value, out var handle)
            ? handle
            : throw new FormatException($"'{value}' is not a valid handle. Expected 'contact:<name>' or 'group:<name>'.");

    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= MaxNameLength && NamePattern().IsMatch(name);

    public override string ToString() => $"{Prefix(Type)}:{Name}";

    private static Handle Create(HandleType type, string name) =>
        IsValidName(name)
            ? new Handle(type, name)
            : throw new ArgumentException(
                $"Handle names must be 1-{MaxNameLength} characters of a-z, 0-9 or '_'.", nameof(name));

    private static string Prefix(HandleType type) => type switch
    {
        HandleType.Contact => "contact",
        HandleType.Group => "group",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
