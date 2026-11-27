using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Events;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Tests.Fakes;

/// <summary>Contacts and groups over lists.</summary>
internal sealed class InMemoryRecipients : IContactRepository, IGroupRepository
{
    public List<Contact> Contacts { get; } = [];

    public List<Group> Groups { get; } = [];

    public Task<Contact?> FindByHandleAsync(Handle handle, CancellationToken cancellationToken) =>
        Task.FromResult(Contacts.SingleOrDefault(c => c.Handle == handle));

    public Task<bool> ExistsAsync(Handle handle, CancellationToken cancellationToken) =>
        Task.FromResult(handle.Type == HandleType.Contact ? Contacts.Any(c => c.Handle == handle) : Groups.Any(g => g.Handle == handle));

    Task<IReadOnlyList<Contact>> IContactRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Contact>>([.. Contacts]);

    public void Add(Contact contact) => Contacts.Add(contact);

    public void Remove(Contact contact) => Contacts.Remove(contact);

    Task<Group?> IGroupRepository.FindByHandleAsync(Handle handle, CancellationToken cancellationToken) =>
        Task.FromResult(Groups.SingleOrDefault(g => g.Handle == handle));

    Task<IReadOnlyList<Group>> IGroupRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Group>>([.. Groups]);

    public void Add(Group group) => Groups.Add(group);

    public void Remove(Group group) => Groups.Remove(group);
}

/// <summary>The audit log over a list (newest last, as inserted).</summary>
internal sealed class InMemoryAuditLog : IAuditLogRepository
{
    public List<AuditEntry> Entries { get; } = [];

    public void Add(AuditEntry entry) => Entries.Add(entry);

    public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(Entries.Count);

    public Task<IReadOnlyList<AuditEntry>> ListNewestFirstAsync(int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>([.. Enumerable.Reverse(Entries).Skip(skip).Take(take)]);
}

/// <summary>Records commits and queued events in one ordered journal, so tests can check "event after commit".</summary>
internal sealed class Journal : IUnitOfWork, IPendingHaEvents
{
    public List<string> Steps { get; } = [];

    public List<HaEvent> Queued { get; } = [];

    public int Saves => Steps.Count(s => s == "save");

    /// <summary>Makes the next commit fail, as a database error would.</summary>
    public bool FailNextSave { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new InvalidOperationException("The database is locked.");
        }

        Steps.Add("save");
        return Task.CompletedTask;
    }

    public void Enqueue(HaEvent haEvent)
    {
        Steps.Add($"enqueue {haEvent.Type}");
        Queued.Add(haEvent);
    }

    public async IAsyncEnumerable<HaEvent> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var haEvent in Queued)
        {
            yield return haEvent;
        }

        await Task.CompletedTask;
    }
}
