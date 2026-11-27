using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Contacts;

/// <summary>
/// Sets a contact's channels for every priority at once (AC53). Each priority may only name channels the contact
/// has an endpoint for. A priority left out routes to nothing. Routing isn't part of <c>/recipients</c>, so the revision
/// doesn't change.
/// </summary>
public sealed class SetContactRouting(
    IContactRepository contacts,
    IUnitOfWork unitOfWork,
    AuditTrail audit,
    ILogger<SetContactRouting> logger)
{
    /// <returns>True when the routing changed.</returns>
    public async Task<Result<bool>> HandleAsync(
        AuditActor actor,
        string contactName,
        IReadOnlyDictionary<Priority, IReadOnlyList<ChannelType>> routing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(routing);
        var found = await contacts.FindAsync(contactName, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contact = found.Value;
        var problems = new List<string>();
        foreach (var (priority, channels) in routing)
        {
            if (!Enum.IsDefined(priority) || channels.Any(c => !Enum.IsDefined(c)))
            {
                problems.Add("Unknown priority or channel.");
                continue;
            }

            if (channels.Distinct().Count() != channels.Count)
            {
                problems.Add($"{ChannelNames.Of(priority)}: each channel can be chosen once.");
            }

            foreach (var missing in contact.ChannelsWithoutEndpoint(channels))
            {
                problems.Add($"{ChannelNames.Of(priority)}: {ChannelNames.Of(missing)} isn't set up for this contact, so it can't be used.");
            }
        }

        if (problems.Count > 0)
        {
            var target = contact.Handle.ToString();
            RecipientLog.Rejected(logger, target, [RecipientFields.Routing]);
            return Error.Validation(new Dictionary<string, string[]> { [RecipientFields.Routing] = [.. problems.Distinct()] });
        }

        var changed = false;
        foreach (var priority in Enum.GetValues<Priority>())
        {
            changed |= contact.SetRouting(priority, routing.GetValueOrDefault(priority, []));
        }

        if (!changed)
        {
            return false;
        }

        var summary = string.Join("; ", Enum.GetValues<Priority>().Select(p =>
            $"{ChannelNames.Of(p)}: {(contact.Routing.ChannelsFor(p) is { Count: > 0 } c ? string.Join(", ", c.Select(ChannelNames.Of)) : "none")}"));
        audit.Record(actor, AuditAction.Update, AuditEntityType.Routing, contact.Handle.ToString(), $"Routing set to {summary}");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        RecipientLog.Changed(logger, actor.HaUserId, "changed the routing of", contact.Handle);
        return true;
    }
}
