using Microsoft.EntityFrameworkCore;
using NotificationApi.Application.Notifications;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Infrastructure.Persistence;

internal sealed class NotificationRepository(NotificationDbContext db) : INotificationRepository
{
    public void Add(Notification notification, IEnumerable<Delivery> deliveries)
    {
        db.Notifications.Add(notification);
        db.Deliveries.AddRange(deliveries);
    }

    public Task<Notification?> FindAsync(NotificationId id, CancellationToken cancellationToken) =>
        db.Notifications.SingleOrDefaultAsync(n => n.Id == id, cancellationToken);

    public Task<Notification?> FindByIdempotencyKeyAsync(string key, DateTimeOffset since, CancellationToken cancellationToken) =>
        db.Notifications
            .Where(n => n.IdempotencyKey == key && n.CreatedAt >= since)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Delivery>> ListDeliveriesAsync(NotificationId id, CancellationToken cancellationToken) =>
        await db.Deliveries.Where(d => d.NotificationId == id).OrderBy(d => d.Position).ToListAsync(cancellationToken);

    public Task<Delivery?> FindDeliveryAsync(DeliveryId id, CancellationToken cancellationToken) =>
        db.Deliveries.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Delivery>> ListDueDeliveriesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await Waiting()
            .Where(d => d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .ThenBy(d => d.CreatedAt)
            .ThenBy(d => d.Position)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<DateTimeOffset?> NextAttemptAtAsync(CancellationToken cancellationToken) =>
        Waiting().OrderBy(d => d.NextAttemptAt).Select(d => d.NextAttemptAt).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Delivery>> ListDeliveriesInStatusAsync(DeliveryStatus status, CancellationToken cancellationToken) =>
        await db.Deliveries.Where(d => d.Status == status).OrderBy(d => d.CreatedAt).ToListAsync(cancellationToken);

    private IQueryable<Delivery> Waiting() =>
        db.Deliveries.Where(d => (d.Status == DeliveryStatus.Queued || d.Status == DeliveryStatus.Retrying) && d.NextAttemptAt != null);
}
