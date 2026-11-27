using NotificationApi.Application.Notifications;
using NotificationApi.Application.Settings;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Settings;

namespace NotificationApi.Application.Tests.Fakes;

/// <summary>The delivery queue over lists. Entities are shared references, as a tracking DbContext would hand out.</summary>
internal sealed class InMemoryNotifications : INotificationRepository
{
    public List<Notification> Notifications { get; } = [];

    public List<Delivery> Deliveries { get; } = [];

    public void Add(Notification notification, IEnumerable<Delivery> deliveries)
    {
        Notifications.Add(notification);
        Deliveries.AddRange(deliveries);
    }

    public Task<Notification?> FindAsync(NotificationId id, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.SingleOrDefault(n => n.Id == id));

    public Task<Notification?> FindByIdempotencyKeyAsync(string key, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.Where(n => n.IdempotencyKey == key && n.CreatedAt >= since).MaxBy(n => n.CreatedAt));

    public Task<IReadOnlyList<Delivery>> ListDeliveriesAsync(NotificationId id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Delivery>>([.. Deliveries.Where(d => d.NotificationId == id).OrderBy(d => d.Position)]);

    public Task<Delivery?> FindDeliveryAsync(DeliveryId id, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.SingleOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<Delivery>> ListDueDeliveriesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Delivery>>([.. Deliveries.Where(d => d.IsDue(now)).OrderBy(d => d.NextAttemptAt).ThenBy(d => d.CreatedAt).ThenBy(d => d.Position).Take(limit)]);

    public Task<DateTimeOffset?> NextAttemptAtAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.Where(d => d.Status is DeliveryStatus.Queued or DeliveryStatus.Retrying).Min(d => d.NextAttemptAt));

    public Task<IReadOnlyList<Delivery>> ListDeliveriesInStatusAsync(DeliveryStatus status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Delivery>>([.. Deliveries.Where(d => d.Status == status)]);
}

internal sealed class InMemorySettings : ISettingsRepository
{
    public AppSettings? Row { get; private set; }

    public Task<AppSettings?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Row);

    public void Add(AppSettings settings) => Row = Row is null ? settings : throw new InvalidOperationException("The settings row already exists.");
}
