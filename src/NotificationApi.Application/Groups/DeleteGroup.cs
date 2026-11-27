using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Groups;

/// <summary>
/// Deletes a group and removes it from every group that contains it (AC56). Its member contacts and groups are not
/// deleted. The UI confirms first, listing the containing groups. Bumps the recipients revision (AC32).
/// </summary>
public sealed class DeleteGroup(
    IGroupRepository groups,
    AuditTrail audit,
    RecordRecipientsChange recordChange,
    ILogger<DeleteGroup> logger)
{
    /// <returns>The groups it was removed from.</returns>
    public async Task<Result<IReadOnlyList<RecipientRef>>> HandleAsync(AuditActor actor, string groupName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var all = await groups.ListAsync(cancellationToken);
        var found = all.Find(groupName);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var group = found.Value;
        var handle = group.Handle.ToString();
        var removedFrom = new List<RecipientRef>();
        foreach (var parent in all.Where(g => g != group).OrderBy(g => g.Handle.ToString(), StringComparer.Ordinal))
        {
            if (parent.RemoveMember(group.Id))
            {
                removedFrom.Add(new RecipientRef(parent.Handle.ToString(), parent.Name.Value));
                audit.Record(actor, AuditAction.Delete, AuditEntityType.Membership, parent.Handle.ToString(), $"Removed {handle} (group deleted)");
            }
        }

        groups.Remove(group);
        var groupsText = removedFrom.Count == 0 ? string.Empty : $"; removed from {string.Join(", ", removedFrom.Select(g => g.Handle))}";
        audit.Record(actor, AuditAction.Delete, AuditEntityType.Group, handle, $"Deleted {handle} ({group.Name}){groupsText}");
        await recordChange.HandleAsync(cancellationToken);

        RecipientLog.Changed(logger, actor.HaUserId, "deleted", group.Handle);
        return removedFrom;
    }
}
