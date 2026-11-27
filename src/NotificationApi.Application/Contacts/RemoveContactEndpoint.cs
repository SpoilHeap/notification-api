using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Contacts;

/// <summary>Removes one of a contact's endpoints, and that channel from its routing (AC53). No revision change.</summary>
public sealed class RemoveContactEndpoint(
    IContactRepository contacts,
    IUnitOfWork unitOfWork,
    AuditTrail audit,
    ILogger<RemoveContactEndpoint> logger)
{
    /// <returns>False when the contact had no such endpoint.</returns>
    public async Task<Result<bool>> HandleAsync(AuditActor actor, string contactName, ChannelType channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var found = await contacts.FindAsync(contactName, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contact = found.Value;
        var masked = contact.EndpointFor(channel)?.MaskedAddress;
        if (!contact.RemoveEndpoint(channel))
        {
            return false;
        }

        audit.Record(actor, AuditAction.Delete, AuditEntityType.Endpoint, contact.Handle.ToString(), $"{ChannelNames.Of(channel)} endpoint {masked} removed (and from routing)");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        RecipientLog.EndpointRemoved(logger, actor.HaUserId, channel, contact.Handle);
        return true;
    }
}
