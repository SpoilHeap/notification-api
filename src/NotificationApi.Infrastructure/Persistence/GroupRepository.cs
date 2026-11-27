using Microsoft.EntityFrameworkCore;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Infrastructure.Persistence;

internal sealed class GroupRepository(NotificationDbContext db) : IGroupRepository
{
    public Task<Group?> FindByHandleAsync(Handle handle, CancellationToken cancellationToken) =>
        db.Groups.SingleOrDefaultAsync(g => g.Handle == handle, cancellationToken);

    public Task<bool> ExistsAsync(Handle handle, CancellationToken cancellationToken) =>
        db.Groups.AnyAsync(g => g.Handle == handle, cancellationToken);

    public async Task<IReadOnlyList<Group>> ListAsync(CancellationToken cancellationToken) =>
        await db.Groups.ToListAsync(cancellationToken);

    public void Add(Group group) => db.Groups.Add(group);

    public void Remove(Group group) => db.Groups.Remove(group);
}
