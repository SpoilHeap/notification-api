using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Contacts;
using NotificationApi.Application.Groups;
using NotificationApi.Application.Instances;
using NotificationApi.Application.Recipients;
using NotificationApi.Application.Tests.Fakes;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Tests;

/// <summary>The contact and group use cases wired to in-memory fakes that share one journal.</summary>
internal sealed class RecipientsHarness
{
    public static readonly AuditActor Actor = new("8f7e6d5c4b3a", "Alice Admin");

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 27, 16, 15, 20, TimeSpan.Zero));

    public InMemoryRecipients Store { get; } = new();

    public InMemoryAuditLog Audit { get; } = new();

    public InMemoryInstances Instances { get; } = new();

    public Journal Journal { get; } = new();

    public List<string> Logs => _logs.SelectMany(l => l()).ToList();

    private readonly List<Func<IEnumerable<string>>> _logs = [];

    public EnsureInstance EnsureInstance => new(Instances, Journal, Time, NullLogger<EnsureInstance>.Instance);

    public RecordRecipientsChange RecordChange => new(EnsureInstance, Journal, Journal, Time);

    public AuditTrail Trail => new(Audit, Time);

    public GetRecipients GetRecipients => new(EnsureInstance, Store, Store);

    public CreateContact CreateContact => new(Store, Trail, RecordChange, Time, Logger<CreateContact>());

    public RenameContact RenameContact => new(Store, Trail, RecordChange, Logger<RenameContact>());

    public SetContactSmsNumber SetSms => new(Store, Journal, Trail, Time, Logger<SetContactSmsNumber>());

    public RemoveContactEndpoint RemoveEndpoint => new(Store, Journal, Trail, Logger<RemoveContactEndpoint>());

    public SetContactRouting SetRouting => new(Store, Journal, Trail, Logger<SetContactRouting>());

    public DeleteContact DeleteContact => new(Store, Store, Trail, RecordChange, Logger<DeleteContact>());

    public ListContacts ListContacts => new(Store);

    public GetContact GetContact => new(Store, Store);

    public CreateGroup CreateGroup => new(Store, Trail, RecordChange, Time, Logger<CreateGroup>());

    public RenameGroup RenameGroup => new(Store, Trail, RecordChange, Logger<RenameGroup>());

    public AddGroupMember AddMember => new(Store, Store, Journal, Trail, Logger<AddGroupMember>());

    public RemoveGroupMember RemoveMember => new(Store, Store, Journal, Trail, Logger<RemoveGroupMember>());

    public DeleteGroup DeleteGroup => new(Store, Trail, RecordChange, Logger<DeleteGroup>());

    public ListGroups ListGroups => new(Store);

    public GetGroup GetGroup => new(Store, Store);

    public GetAuditLog GetAuditLog => new(Audit);

    public async Task<string> RevisionAsync() => (await GetRecipients.HandleAsync(CancellationToken.None)).Revision;

    public async Task ContactAsync(string handle, string name, string? sms = null)
    {
        (await CreateContact.HandleAsync(Actor, new CreateRecipientCommand(handle, name), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        if (sms is not null)
        {
            (await SetSms.HandleAsync(Actor, handle, sms, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }
    }

    public async Task GroupAsync(string handle, string name, params string[] members)
    {
        (await CreateGroup.HandleAsync(Actor, new CreateRecipientCommand(handle, name), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        foreach (var member in members)
        {
            (await AddMember.HandleAsync(Actor, handle, member, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }
    }

    private ListLogger<T> Logger<T>()
    {
        var logger = new ListLogger<T>();
        _logs.Add(() => logger.Entries.Select(e => e.Text));
        return logger;
    }
}
