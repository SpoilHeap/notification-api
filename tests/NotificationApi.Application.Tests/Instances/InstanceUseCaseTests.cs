using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.Events;
using NotificationApi.Application.Instances;
using NotificationApi.Application.Recipients;
using NotificationApi.Application.Tests.Fakes;

namespace NotificationApi.Application.Tests.Instances;

public sealed class InstanceUseCaseTests
{
    private readonly InMemoryInstances _instances = new();
    private readonly InMemoryRecipients _recipients = new();
    private readonly Journal _queue = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 16, 15, 20, TimeSpan.Zero));

    private EnsureInstance Ensure => new(_instances, _instances, _time, NullLogger<EnsureInstance>.Instance);

    [Fact]
    public async Task AC44_The_instance_is_created_once_and_then_reused()
    {
        var first = await Ensure.HandleAsync(TestContext.Current.CancellationToken);
        var second = await Ensure.HandleAsync(TestContext.Current.CancellationToken);

        second.Id.ShouldBe(first.Id);
        _instances.Rows.ShouldHaveSingleItem();
        _instances.Saves.ShouldBe(1);
        first.CreatedAt.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public async Task AC40_Recipients_start_empty_at_revision_1()
    {
        var list = await new GetRecipients(Ensure, _recipients, _recipients).HandleAsync(TestContext.Current.CancellationToken);

        list.Revision.ShouldBe("1");
        list.Recipients.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC43_Recording_a_change_bumps_and_saves_the_revision()
    {
        var useCase = RecordChange();

        (await useCase.HandleAsync(TestContext.Current.CancellationToken)).ShouldBe("2");
        (await useCase.HandleAsync(TestContext.Current.CancellationToken)).ShouldBe("3");

        (await new GetRecipients(Ensure, _recipients, _recipients).HandleAsync(TestContext.Current.CancellationToken)).Revision.ShouldBe("3");
        _instances.Saves.ShouldBe(3); // creation plus two changes
    }

    [Fact]
    public async Task AC45_Recording_a_change_queues_recipients_changed_with_the_new_revision()
    {
        var useCase = RecordChange();

        var revision = await useCase.HandleAsync(TestContext.Current.CancellationToken);

        var queued = _queue.Queued.ShouldHaveSingleItem();
        queued.Type.ShouldBe(HaEventTypes.RecipientsChanged);
        queued.Data.ShouldBe(new RecipientsChangedData(revision));
        queued.OccurredAt.ShouldBe(_time.GetUtcNow());
        (await new GetRecipients(Ensure, _recipients, _recipients).HandleAsync(TestContext.Current.CancellationToken)).Revision.ShouldBe(revision);
    }

    [Fact]
    public async Task AC32_No_event_is_queued_when_the_commit_fails()
    {
        var useCase = new RecordRecipientsChange(Ensure, _queue, _queue, _time);
        await Ensure.HandleAsync(TestContext.Current.CancellationToken);
        _queue.FailNextSave = true;

        await Should.ThrowAsync<InvalidOperationException>(() => useCase.HandleAsync(TestContext.Current.CancellationToken));

        _queue.Queued.ShouldBeEmpty();
    }

    private RecordRecipientsChange RecordChange() => new(Ensure, _instances, _queue, _time);
}
