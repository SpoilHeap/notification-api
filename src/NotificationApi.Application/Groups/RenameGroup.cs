using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Groups;

/// <summary>Changes a group's display name; the handle stays (AC51). A rename bumps the recipients revision (AC32, AC43).</summary>
public sealed class RenameGroup(
    IGroupRepository groups,
    AuditTrail audit,
    RecordRecipientsChange recordChange,
    ILogger<RenameGroup> logger)
{
    /// <returns>True when the name changed.</returns>
    public async Task<Result<bool>> HandleAsync(AuditActor actor, string groupName, string? newName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var found = await groups.FindAsync(groupName, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var group = found.Value;
        var errors = new Dictionary<string, string[]>();
        if (!RecipientValidator.TryName(newName, errors, out var name))
        {
            var target = group.Handle.ToString();
            RecipientLog.Rejected(logger, target, errors.Keys);
            return Error.Validation(errors);
        }

        var previous = group.Name;
        if (!group.Rename(name))
        {
            return false;
        }

        audit.Record(actor, AuditAction.Update, AuditEntityType.Group, group.Handle.ToString(), $"Renamed from '{previous}' to '{name}'");
        await recordChange.HandleAsync(cancellationToken);
        RecipientLog.Changed(logger, actor.HaUserId, "renamed", group.Handle);
        return true;
    }
}
