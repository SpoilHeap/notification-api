using NotificationApi.Application.Instances;

namespace NotificationApi.Application.Recipients;

/// <summary><c>GET /recipients</c> (spec §4.2). Contacts and groups don't exist yet, so the list is empty.</summary>
public sealed class GetRecipients(EnsureInstance ensureInstance)
{
    public async Task<RecipientList> HandleAsync(CancellationToken cancellationToken)
    {
        var instance = await ensureInstance.HandleAsync(cancellationToken);
        // TODO(contacts and groups): list them here, from the same unit of work as the revision.
        return new RecipientList(instance.RecipientsRevision, []);
    }
}
