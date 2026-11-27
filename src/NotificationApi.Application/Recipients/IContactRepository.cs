using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Recipients;

public interface IContactRepository
{
    Task<Contact?> FindByHandleAsync(Handle handle, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Handle handle, CancellationToken cancellationToken);

    Task<IReadOnlyList<Contact>> ListAsync(CancellationToken cancellationToken);

    void Add(Contact contact);

    void Remove(Contact contact);
}
