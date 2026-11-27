using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Notifications;

/// <summary>Notifications and their deliveries: the persisted delivery queue (ADR 0014).</summary>
public interface INotificationRepository
{
    void Add(Notification notification, IEnumerable<Delivery> deliveries);

    Task<Notification?> FindAsync(NotificationId id, CancellationToken cancellationToken);

    /// <summary>The newest notification accepted with <paramref name="key"/> at or after <paramref name="since"/>.</summary>
    Task<Notification?> FindByIdempotencyKeyAsync(string key, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>The notification's deliveries in <see cref="Delivery.Position"/> order.</summary>
    Task<IReadOnlyList<Delivery>> ListDeliveriesAsync(NotificationId id, CancellationToken cancellationToken);

    Task<Delivery?> FindDeliveryAsync(DeliveryId id, CancellationToken cancellationToken);

    /// <summary>Queued or retrying deliveries due at <paramref name="now"/>, earliest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Delivery>> ListDueDeliveriesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>When the next queued or retrying delivery is due, or null if there are none.</summary>
    Task<DateTimeOffset?> NextAttemptAtAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Delivery>> ListDeliveriesInStatusAsync(DeliveryStatus status, CancellationToken cancellationToken);
}
