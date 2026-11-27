using Microsoft.Extensions.Logging;
using NotificationApi.Application.Common;
using NotificationApi.Application.Events;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Notifications;

/// <summary>
/// Runs once when the delivery worker starts (AC23, ADR 0014). Queued and retrying deliveries need nothing: they are
/// in the database and the worker picks them up. A delivery still <c>sending</c> was cut off mid-attempt by a crash,
/// so whether the provider got it is unknown. It is marked <c>failed</c> (with a <c>delivery.failed</c> event) rather
/// than sent again, so nothing is ever sent twice and HA still learns the outcome.
/// </summary>
public sealed partial class RecoverInterruptedDeliveries(
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    IPendingHaEvents events,
    TimeProvider time,
    ILogger<RecoverInterruptedDeliveries> logger)
{
    public const string InterruptedError =
        "The add-on stopped while this delivery was being sent, so it may or may not have arrived. It was not sent again.";

    /// <returns>How many deliveries were marked failed.</returns>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var interrupted = await notifications.ListDeliveriesInStatusAsync(DeliveryStatus.Sending, cancellationToken);
        if (interrupted.Count == 0)
        {
            return 0;
        }

        var now = time.GetUtcNow();
        foreach (var delivery in interrupted)
        {
            delivery.Fail(InterruptedError, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var delivery in interrupted)
        {
            var notification = await notifications.FindAsync(delivery.NotificationId, cancellationToken);
            if (notification is not null)
            {
                events.Enqueue(HaEvent.DeliveryCompleted(notification, delivery, time));
            }

            LogInterrupted(logger, delivery.Id, delivery.Contact.ToString());
        }

        return interrupted.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delivery {DeliveryId} to {Contact} was interrupted by a restart while sending; marked failed and not sent again.")]
    private static partial void LogInterrupted(ILogger logger, DeliveryId deliveryId, string contact);
}
