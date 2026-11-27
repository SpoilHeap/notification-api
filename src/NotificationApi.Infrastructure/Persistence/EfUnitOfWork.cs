using NotificationApi.Application.Common;

namespace NotificationApi.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(NotificationDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
