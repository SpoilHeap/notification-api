using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Notifications;

/// <summary>The <c>202</c> answer to <c>POST /notify</c> (spec §4.1).</summary>
public sealed record NotificationReceipt(NotificationId NotificationId, IReadOnlyList<DeliveryReceipt> Deliveries)
{
    /// <summary>
    /// Built from what was stored, so a replayed idempotency key gets the same answer as the first request. Every
    /// status is <c>queued</c>, as it was then (spec §4.1 "202 body").
    /// </summary>
    public static NotificationReceipt From(Notification notification, IEnumerable<Delivery> deliveries)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(deliveries);
        return new NotificationReceipt(
            notification.Id,
            [.. deliveries.OrderBy(d => d.Position).Select(d => new DeliveryReceipt(d.Id, d.Contact.Name, d.Via.ToString(), WireNames.Of(d.Channel), QueuedStatus))]);
    }

    public const string QueuedStatus = "queued";
}

/// <param name="DeliveryId">E.g. <c>dlv_019a…</c>.</param>
/// <param name="Contact">The contact's handle name, without <c>contact:</c> (e.g. <c>alice</c>).</param>
/// <param name="Via">The top-level handle from <c>to</c> (e.g. <c>group:family</c>).</param>
/// <param name="Channel"><c>telegram</c> or <c>sms</c>.</param>
/// <param name="Status">Always <c>queued</c> in the 202.</param>
public sealed record DeliveryReceipt(DeliveryId DeliveryId, string Contact, string Via, string Channel, string Status);
