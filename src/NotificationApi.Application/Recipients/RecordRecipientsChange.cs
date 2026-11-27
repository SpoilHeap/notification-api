using NotificationApi.Application.Common;
using NotificationApi.Application.Events;
using NotificationApi.Application.Instances;

namespace NotificationApi.Application.Recipients;

/// <summary>
/// Call after creating, renaming or deleting a contact or group (spec AC32, AC43, AC45). It bumps the recipients
/// revision, commits the unit of work (so the change and the new revision are saved together), then sends a
/// best-effort <c>recipients.changed</c> event carrying the new revision.
/// </summary>
public sealed class RecordRecipientsChange(
    EnsureInstance ensureInstance,
    IUnitOfWork unitOfWork,
    PublishHaEvent publishHaEvent,
    TimeProvider time)
{
    /// <returns>The new revision.</returns>
    public async Task<string> HandleAsync(CancellationToken cancellationToken)
    {
        var instance = await ensureInstance.HandleAsync(cancellationToken);
        var revision = instance.BumpRecipientsRevision();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await publishHaEvent.HandleAsync(HaEvent.RecipientsChanged(revision, time), cancellationToken);
        return revision;
    }
}
