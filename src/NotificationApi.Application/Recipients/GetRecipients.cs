using NotificationApi.Application.Instances;

namespace NotificationApi.Application.Recipients;

/// <summary><c>GET /recipients</c> (spec §4.2, AC40): every contact and group, contacts first, then by handle.</summary>
public sealed class GetRecipients(EnsureInstance ensureInstance, IContactRepository contacts, IGroupRepository groups)
{
    public async Task<RecipientList> HandleAsync(CancellationToken cancellationToken)
    {
        // Read through the same scope (and so the same DbContext) as the revision.
        var instance = await ensureInstance.HandleAsync(cancellationToken);
        var contactList = await contacts.ListAsync(cancellationToken);
        var groupList = await groups.ListAsync(cancellationToken);

        IEnumerable<RecipientSummary> all =
        [
            .. contactList
                .Select(c => new RecipientSummary(c.Handle.ToString(), c.Name.Value, RecipientSummary.ContactType))
                .OrderBy(r => r.Handle, StringComparer.Ordinal),
            .. groupList
                .Select(g => new RecipientSummary(g.Handle.ToString(), g.Name.Value, RecipientSummary.GroupType))
                .OrderBy(r => r.Handle, StringComparer.Ordinal),
        ];
        return new RecipientList(instance.RecipientsRevision, [.. all]);
    }
}
