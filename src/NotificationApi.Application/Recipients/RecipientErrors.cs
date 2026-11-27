using NotificationApi.Application.Common;

namespace NotificationApi.Application.Recipients;

/// <summary>Form field keys for contact and group commands (the UI shows each message next to its field).</summary>
public static class RecipientFields
{
    public const string Handle = "handle";
    public const string Name = "name";
    public const string PhoneNumber = "phone_number";
    public const string Routing = "routing";
    public const string Member = "member";
}

public static class RecipientErrors
{
    public const string ContactNotFoundCode = "contact.not_found";
    public const string GroupNotFoundCode = "group.not_found";

    public static Error ContactNotFound(string name) => new(ContactNotFoundCode, $"There is no contact '{name}'.");

    public static Error GroupNotFound(string name) => new(GroupNotFoundCode, $"There is no group '{name}'.");

    public static Error Field(string field, string message) =>
        Error.Validation(new Dictionary<string, string[]> { [field] = [message] });
}
