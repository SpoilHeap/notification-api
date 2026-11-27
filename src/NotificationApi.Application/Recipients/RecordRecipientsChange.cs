using NotificationApi.Application.Common;
using NotificationApi.Application.Events;
using NotificationApi.Application.Instances;

namespace NotificationApi.Application.Recipients;

/// <summary>
/// Call after creating, renaming or deleting a contact or group (spec AC32, AC43, AC45), instead of committing. It
/// bumps the recipients revision and commits the unit of work, so the change, its audit entry and the new revision
/// are saved together. Only then is a <c>recipients.changed</c> event carrying the new revision queued for the
/// background sender: the caller doesn't wait for HA, and no event is sent for a change that wasn't saved.
/// </summary>
public sealed class RecordRecipientsChange(
    EnsureInstance ensureInstance,
    IUnitOfWork unitOfWork,
    IPendingHaEvents events,
    TimeProvider time)
{
    /// <returns>The new revision.</returns>
    public async Task<string> HandleAsync(CancellationToken cancellationToken)
    {
        var instance = await ensureInstance.HandleAsync(cancellationToken);
        var revision = instance.BumpRecipientsRevision();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        events.Enqueue(HaEvent.RecipientsChanged(revision, time));
        return revision;
    }
}
