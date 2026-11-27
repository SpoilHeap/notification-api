using NotificationApi.Application.Recipients;

namespace NotificationApi.Application.Contacts;

/// <summary>The Contacts list, sorted by handle, with masked endpoints.</summary>
public sealed class ListContacts(IContactRepository contacts)
{
    public async Task<IReadOnlyList<ContactSummary>> HandleAsync(CancellationToken cancellationToken) =>
    [
        .. (await contacts.ListAsync(cancellationToken))
            .OrderBy(c => c.Handle.ToString(), StringComparer.Ordinal)
            .Select(c => new ContactSummary(
                c.Handle.ToString(),
                c.Handle.Name,
                c.Name.Value,
                [.. c.Endpoints.OrderBy(e => e.Channel).Select(EndpointView.From)])),
    ];
}
