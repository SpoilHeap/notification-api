using Microsoft.Extensions.Logging;
using NotificationApi.Application.Common;
using NotificationApi.Application.Events;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Notifications;

/// <summary>
/// Called by the delivery worker when an attempt crashed unexpectedly (for example, saving its outcome failed after
/// the send). The delivery is left <c>sending</c>, and whether the provider got it is unknown, so, as with the
/// restart rule (ADR 0014), it is marked <c>failed</c> with a <c>delivery.failed</c> event and never sent again.
/// Runs in a fresh scope, so a broken <c>DbContext</c> from the crashed attempt isn't reused. If this fails too, the
/// delivery stays <c>sending</c> and <see cref="RecoverInterruptedDeliveries"/> handles it on the next start.
/// </summary>
public sealed partial class AbandonDelivery(
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    IPendingHaEvents events,
    TimeProvider time,
    ILogger<AbandonDelivery> logger)
{
    public const string AbandonedError =
        "An unexpected error interrupted this delivery, so it may or may not have arrived. It was not sent again.";

    /// <returns>True if the delivery was marked failed; false if it wasn't stuck in <c>sending</c>.</returns>
    public async Task<bool> HandleAsync(DeliveryId deliveryId, CancellationToken cancellationToken)
    {
        var delivery = await notifications.FindDeliveryAsync(deliveryId, cancellationToken);
        if (delivery is not { Status: DeliveryStatus.Sending })
        {
            return false; // the attempt got as far as recording an outcome
        }

        delivery.Fail(AbandonedError, time.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (await notifications.FindAsync(delivery.NotificationId, cancellationToken) is { } notification)
        {
            events.Enqueue(HaEvent.DeliveryCompleted(notification, delivery, time));
        }

        LogAbandoned(logger, delivery.Id, delivery.Contact.ToString());
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delivery {DeliveryId} to {Contact} was interrupted by an unexpected error while sending; marked failed and not sent again.")]
    private static partial void LogAbandoned(ILogger logger, DeliveryId deliveryId, string contact);
}
