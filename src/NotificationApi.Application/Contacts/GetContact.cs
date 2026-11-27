using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Contacts;

/// <summary>One contact for its edit and delete pages, with the groups that contain it.</summary>
public sealed class GetContact(IContactRepository contacts, IGroupRepository groups)
{
    public async Task<Result<ContactDetails>> HandleAsync(string contactName, CancellationToken cancellationToken)
    {
        var found = await contacts.FindAsync(contactName, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contact = found.Value;
        var memberOf = (await groups.ListAsync(cancellationToken))
            .Where(g => g.Contains(contact.Id))
            .OrderBy(g => g.Handle.ToString(), StringComparer.Ordinal)
            .Select(g => new RecipientRef(g.Handle.ToString(), g.Name.Value))
            .ToList();

        return new ContactDetails(
            contact.Handle.ToString(),
            contact.Handle.Name,
            contact.Name.Value,
            [.. contact.Endpoints.OrderBy(e => e.Channel).Select(EndpointView.From)],
            Enum.GetValues<Priority>().ToDictionary(p => p, contact.Routing.ChannelsFor),
            memberOf);
    }
}
