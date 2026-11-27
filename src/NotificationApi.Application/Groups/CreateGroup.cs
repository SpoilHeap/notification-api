using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Groups;

/// <summary>Creates an empty group with a unique, permanent handle (AC54, AC57). Bumps the recipients revision (AC32).</summary>
public sealed class CreateGroup(
    IGroupRepository groups,
    AuditTrail audit,
    RecordRecipientsChange recordChange,
    TimeProvider time,
    ILogger<CreateGroup> logger)
{
    public async Task<Result<Handle>> HandleAsync(AuditActor actor, CreateRecipientCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(command);

        var errors = new Dictionary<string, string[]>();
        RecipientValidator.TryHandle(HandleType.Group, command.Handle, errors, out var handle);
        RecipientValidator.TryName(command.Name, errors, out var name);
        if (handle is not null && await groups.ExistsAsync(handle, cancellationToken))
        {
            errors[RecipientFields.Handle] = [$"{handle} already exists. Handles are unique and can't be reused while it exists."];
        }

        if (errors.Count > 0 || handle is null || name is null)
        {
            var target = handle?.ToString() ?? "a new group";
            RecipientLog.Rejected(logger, target, errors.Keys);
            return Error.Validation(errors);
        }

        groups.Add(Group.Create(handle, name, time.GetUtcNow()));
        audit.Record(actor, AuditAction.Create, AuditEntityType.Group, handle.ToString(), $"Created {handle} ({name})");
        await recordChange.HandleAsync(cancellationToken);

        RecipientLog.Changed(logger, actor.HaUserId, "created", handle);
        return handle;
    }
}
