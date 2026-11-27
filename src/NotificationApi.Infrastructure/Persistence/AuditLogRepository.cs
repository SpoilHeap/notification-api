using Microsoft.EntityFrameworkCore;
using NotificationApi.Application.Audit;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Infrastructure.Persistence;

internal sealed class AuditLogRepository(NotificationDbContext db) : IAuditLogRepository
{
    public void Add(AuditEntry entry) => db.AuditLog.Add(entry);

    public Task<int> CountAsync(CancellationToken cancellationToken) => db.AuditLog.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<AuditEntry>> ListNewestFirstAsync(int skip, int take, CancellationToken cancellationToken) =>
        await db.AuditLog.AsNoTracking().OrderByDescending(e => e.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);
}
