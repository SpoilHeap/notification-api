using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Contacts;

/// <summary>
/// Deletes a contact and removes it from every group that contains it (AC56). The UI confirms first, listing those
/// groups. Bumps the recipients revision (AC32).
/// </summary>
public sealed class DeleteContact(
    IContactRepository contacts,
    IGroupRepository groups,
    AuditTrail audit,
    RecordRecipientsChange recordChange,
    ILogger<DeleteContact> logger)
{
    /// <returns>The groups it was removed from.</returns>
    public async Task<Result<IReadOnlyList<RecipientRef>>> HandleAsync(AuditActor actor, string contactName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var found = await contacts.FindAsync(contactName, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contact = found.Value;
        var handle = contact.Handle.ToString();
        var removedFrom = new List<RecipientRef>();
        foreach (var group in (await groups.ListAsync(cancellationToken)).OrderBy(g => g.Handle.ToString(), StringComparer.Ordinal))
        {
            if (group.RemoveMember(contact.Id))
            {
                removedFrom.Add(new RecipientRef(group.Handle.ToString(), group.Name.Value));
                audit.Record(actor, AuditAction.Delete, AuditEntityType.Membership, group.Handle.ToString(), $"Removed {handle} (contact deleted)");
            }
        }

        contacts.Remove(contact);
        var groupsText = removedFrom.Count == 0 ? string.Empty : $"; removed from {string.Join(", ", removedFrom.Select(g => g.Handle))}";
        audit.Record(actor, AuditAction.Delete, AuditEntityType.Contact, handle, $"Deleted {handle} ({contact.Name}){groupsText}");
        await recordChange.HandleAsync(cancellationToken);

        RecipientLog.Changed(logger, actor.HaUserId, "deleted", contact.Handle);
        return removedFrom;
    }
}
