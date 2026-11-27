using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Recipients;

public interface IGroupRepository
{
    Task<Group?> FindByHandleAsync(Handle handle, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Handle handle, CancellationToken cancellationToken);

    /// <summary>Every group with its members. A home has few, so membership questions are answered in memory.</summary>
    Task<IReadOnlyList<Group>> ListAsync(CancellationToken cancellationToken);

    void Add(Group group);

    void Remove(Group group);
}
