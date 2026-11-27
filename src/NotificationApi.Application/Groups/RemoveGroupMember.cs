using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Groups;

/// <summary>Removes a member from a group. A removed group keeps its own members. No revision change.</summary>
public sealed class RemoveGroupMember(
    IGroupRepository groups,
    IContactRepository contacts,
    IUnitOfWork unitOfWork,
    AuditTrail audit,
    ILogger<RemoveGroupMember> logger)
{
    /// <returns>False when it wasn't a member.</returns>
    public async Task<Result<bool>> HandleAsync(AuditActor actor, string groupName, string? memberHandle, CancellationToken cancellationToken)
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
            return RecipientErrors.Field(RecipientFields.Member, "Choose a member to remove.");
        }

        var removed = handle.Type == HandleType.Contact
            ? await contacts.FindByHandleAsync(handle, cancellationToken) is { } contact && group.RemoveMember(contact.Id)
            : all.SingleOrDefault(g => g.Handle == handle) is { } member && group.RemoveMember(member.Id);
        if (!removed)
        {
            return false;
        }

        audit.Record(actor, AuditAction.Delete, AuditEntityType.Membership, group.Handle.ToString(), $"Removed {handle}");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        RecipientLog.MembershipChanged(logger, actor.HaUserId, group.Handle, "removed", handle);
        return true;
    }
}
