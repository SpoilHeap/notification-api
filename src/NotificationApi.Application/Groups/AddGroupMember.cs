using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Groups;

/// <summary>
/// Adds a contact or another group to a group (AC54). Self-membership, a duplicate member and any cycle, direct or
/// indirect, are rejected with a message naming the path (AC55). Membership isn't part of <c>/recipients</c>, so the
/// revision doesn't change.
/// </summary>
public sealed class AddGroupMember(
    IGroupRepository groups,
    IContactRepository contacts,
    IUnitOfWork unitOfWork,
    AuditTrail audit,
    ILogger<AddGroupMember> logger)
{
    /// <returns>The member that was added.</returns>
    public async Task<Result<RecipientRef>> HandleAsync(AuditActor actor, string groupName, string? memberHandle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var all = await groups.ListAsync(cancellationToken);
        var found = all.Find(groupName);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var group = found.Value;
        if (!Handle.TryParse(memberHandle?.Trim(), out var handle))
        {
            return Reject(group, "Choose a contact or group to add.");
        }

        RecipientRef added;
        if (handle.Type == HandleType.Contact)
        {
            var contact = await contacts.FindByHandleAsync(handle, cancellationToken);
            if (contact is null)
            {
                return Reject(group, $"There is no {handle}.");
            }

            if (group.ProblemAdding(contact) is not null)
            {
                return Reject(group, $"{handle} is already a member of {group.Handle}.");
            }

            group.AddMember(contact);
            added = new RecipientRef(handle.ToString(), contact.Name.Value);
        }
        else
        {
            var member = all.SingleOrDefault(g => g.Handle == handle);
            if (member is null)
            {
                return Reject(group, $"There is no {handle}.");
            }

            var graph = new GroupGraph(all);
            switch (group.ProblemAdding(member, graph))
            {
                case MembershipProblem.Self:
                    return Reject(group, "A group can't be a member of itself.");
                case MembershipProblem.AlreadyMember:
                    return Reject(group, $"{handle} is already a member of {group.Handle}.");
                case MembershipProblem.Cycle:
                    var path = graph.PathBetween(member.Id, group.Id)!.Select(id => all.Single(g => g.Id == id).Handle.ToString());
                    return Reject(group, $"{handle} already contains {group.Handle} ({string.Join(" → ", path)}), so adding it would create a loop.");
                default:
                    break;
            }

            group.AddMember(member, graph);
            added = new RecipientRef(handle.ToString(), member.Name.Value);
        }

        audit.Record(actor, AuditAction.Create, AuditEntityType.Membership, group.Handle.ToString(), $"Added {handle}");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        RecipientLog.MembershipChanged(logger, actor.HaUserId, group.Handle, "added", handle);
        return added;
    }

    private Error Reject(Group group, string message)
    {
        var target = group.Handle.ToString();
        RecipientLog.Rejected(logger, target, [RecipientFields.Member]);
        return RecipientErrors.Field(RecipientFields.Member, message);
    }
}
