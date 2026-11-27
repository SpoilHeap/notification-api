using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Contacts;

/// <summary>Changes a contact's display name; the handle stays (AC51). A rename bumps the recipients revision (AC32, AC43).</summary>
public sealed class RenameContact(
    IContactRepository contacts,
    AuditTrail audit,
    RecordRecipientsChange recordChange,
    ILogger<RenameContact> logger)
{
    /// <returns>True when the name changed.</returns>
    public async Task<Result<bool>> HandleAsync(AuditActor actor, string contactName, string? newName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var found = await contacts.FindAsync(contactName, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contact = found.Value;
        var errors = new Dictionary<string, string[]>();
        if (!RecipientValidator.TryName(newName, errors, out var name))
        {
            var target = contact.Handle.ToString();
            RecipientLog.Rejected(logger, target, errors.Keys);
            return Error.Validation(errors);
        }

        var previous = contact.Name;
        if (!contact.Rename(name))
        {
            return false;
        }

        audit.Record(actor, AuditAction.Update, AuditEntityType.Contact, contact.Handle.ToString(), $"Renamed from '{previous}' to '{name}'");
        await recordChange.HandleAsync(cancellationToken);
        RecipientLog.Changed(logger, actor.HaUserId, "renamed", contact.Handle);
        return true;
    }
}
