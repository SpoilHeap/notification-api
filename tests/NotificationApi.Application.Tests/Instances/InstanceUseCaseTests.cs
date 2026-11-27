using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Events;
using NotificationApi.Application.Instances;
using NotificationApi.Application.Recipients;
using NotificationApi.Application.Tests.Fakes;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.Tests.Instances;

public sealed class InstanceUseCaseTests
{
    private readonly InMemoryInstances _instances = new();
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
        var list = await new GetRecipients(Ensure).HandleAsync(TestContext.Current.CancellationToken);

        list.Revision.ShouldBe("1");
        list.Recipients.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC43_Recording_a_change_bumps_and_saves_the_revision()
    {
        var (useCase, _) = RecordChange();

        (await useCase.HandleAsync(TestContext.Current.CancellationToken)).ShouldBe("2");
        (await useCase.HandleAsync(TestContext.Current.CancellationToken)).ShouldBe("3");

        (await new GetRecipients(Ensure).HandleAsync(TestContext.Current.CancellationToken)).Revision.ShouldBe("3");
        _instances.Saves.ShouldBe(3); // creation plus two changes
    }

    [Fact]
    public async Task AC45_Recording_a_change_sends_recipients_changed_with_the_new_revision()
    {
        var (useCase, publisher) = RecordChange(registerWebhook: true);

        var revision = await useCase.HandleAsync(TestContext.Current.CancellationToken);

        var sent = publisher.Sent.ShouldHaveSingleItem().Event;
        sent.Type.ShouldBe(HaEventTypes.RecipientsChanged);
        sent.Data.ShouldBe(new RecipientsChangedData(revision));
        (await new GetRecipients(Ensure).HandleAsync(TestContext.Current.CancellationToken)).Revision.ShouldBe(revision);
    }

    [Fact]
    public async Task AC34_A_failed_event_does_not_undo_the_change()
    {
        var (useCase, publisher) = RecordChange(registerWebhook: true);
        publisher.Result = WebhookSendResult.Failed("the webhook is unreachable");

        var revision = await useCase.HandleAsync(TestContext.Current.CancellationToken);

        revision.ShouldBe("2");
        (await new GetRecipients(Ensure).HandleAsync(TestContext.Current.CancellationToken)).Revision.ShouldBe("2");
    }

    private (RecordRecipientsChange UseCase, FakeHaEventPublisher Publisher) RecordChange(bool registerWebhook = false)
    {
        var clients = new InMemoryApiClients();
        var protector = new FakeSecretProtector();
        if (registerWebhook)
        {
            var client = ApiClient.Create(ApiClientNames.HomeAssistant, ApiToken.Generate(), "protected:x", _time.GetUtcNow());
            client.RegisterWebhook(new Uri("http://homeassistant:8123/api/webhook/x"), protector.Protect(Convert.ToBase64String(new byte[32])), _time.GetUtcNow());
            clients.Add(client);
        }

        var publisher = new FakeHaEventPublisher();
        var publish = new PublishHaEvent(clients, protector, publisher, NullLogger<PublishHaEvent>.Instance);
        return (new RecordRecipientsChange(Ensure, _instances, publish, _time), publisher);
    }
}
