using System.Diagnostics.CodeAnalysis;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Recipients;

/// <summary>Create input for a contact or group, before validation.</summary>
/// <param name="Handle">The name part (<c>alice</c>). A matching <c>contact:</c>/<c>group:</c> prefix is accepted too.</param>
/// <param name="Name">The display name.</param>
public sealed record CreateRecipientCommand(string? Handle, string? Name);

/// <summary>Hand-written validation shared by the contact and group use cases. Keys are <see cref="RecipientFields"/>.</summary>
public static class RecipientValidator
{
    public static bool TryHandle(HandleType type, string? input, Dictionary<string, string[]> errors, [NotNullWhen(true)] out Handle? handle)
    {
        ArgumentNullException.ThrowIfNull(errors);
        handle = null;
        var prefix = type == HandleType.Contact ? "contact:" : "group:";
        var name = input?.Trim() ?? string.Empty;
        if (name.StartsWith(prefix, StringComparison.Ordinal))
        {
            name = name[prefix.Length..];
        }

        if (name.Length == 0)
        {
            errors[RecipientFields.Handle] = ["The handle is required."];
            return false;
        }

        if (!Domain.Recipients.Handle.IsValidName(name))
        {
            errors[RecipientFields.Handle] =
                [$"Use 1-{Domain.Recipients.Handle.MaxNameLength} lower-case letters, digits or underscores (a-z, 0-9, _)."];
            return false;
        }

        handle = type == HandleType.Contact ? Domain.Recipients.Handle.Contact(name) : Domain.Recipients.Handle.Group(name);
        return true;
    }

    public static bool TryName(string? input, Dictionary<string, string[]> errors, [NotNullWhen(true)] out DisplayName? name)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (DisplayName.TryCreate(input, out name))
        {
            return true;
        }

        errors[RecipientFields.Name] = string.IsNullOrWhiteSpace(input)
            ? ["The name is required."]
            : [$"The name must be at most {DisplayName.MaxLength} characters on one line."];
        return false;
    }

    /// <summary>A handle name from a URL, or null if it can't be one (treated as not found).</summary>
    public static Handle? ParseExisting(HandleType type, string? name) =>
        !Domain.Recipients.Handle.IsValidName(name) ? null
        : type == HandleType.Contact ? Domain.Recipients.Handle.Contact(name!) : Domain.Recipients.Handle.Group(name!);
}
