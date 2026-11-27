using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Contacts;

internal static class ContactLookup
{
    /// <summary>The contact named in a URL, or <see cref="RecipientErrors.ContactNotFound"/>.</summary>
    public static async Task<Result<Contact>> FindAsync(this IContactRepository contacts, string? name, CancellationToken cancellationToken)
    {
        var handle = RecipientValidator.ParseExisting(HandleType.Contact, name);
        var contact = handle is null ? null : await contacts.FindByHandleAsync(handle, cancellationToken);
        return contact is null ? RecipientErrors.ContactNotFound(name ?? string.Empty) : contact;
    }
}
