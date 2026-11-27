using Microsoft.EntityFrameworkCore;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Infrastructure.Persistence;

internal sealed class ContactRepository(NotificationDbContext db) : IContactRepository
{
    public Task<Contact?> FindByHandleAsync(Handle handle, CancellationToken cancellationToken) =>
        db.Contacts.SingleOrDefaultAsync(c => c.Handle == handle, cancellationToken);

    public Task<bool> ExistsAsync(Handle handle, CancellationToken cancellationToken) =>
        db.Contacts.AnyAsync(c => c.Handle == handle, cancellationToken);

    public async Task<IReadOnlyList<Contact>> ListAsync(CancellationToken cancellationToken) =>
        await db.Contacts.ToListAsync(cancellationToken);

    public void Add(Contact contact) => db.Contacts.Add(contact);

    public void Remove(Contact contact) => db.Contacts.Remove(contact);
}
