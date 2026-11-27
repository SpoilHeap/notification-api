using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.Events;

namespace NotificationApi.Application.Tests.Events;

public sealed class HaEventTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 17, 15, 20, TimeSpan.FromHours(1)));

    [Fact]
    public void Events_get_a_unique_evt_id_of_32_hex_digits_and_the_time_in_utc()
    {
        var first = HaEvent.WebhookTest(_time);
        var second = HaEvent.WebhookTest(_time);

        first.Id.ShouldMatch("^evt_[0-9a-f]{32}$");
        second.Id.ShouldNotBe(first.Id);
        first.OccurredAt.ShouldBe(new DateTimeOffset(2026, 11, 27, 16, 15, 20, TimeSpan.Zero));
        first.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Webhook_test_events_have_empty_data()
    {
        var haEvent = HaEvent.WebhookTest(_time);

        haEvent.Type.ShouldBe("webhook.test");
        haEvent.Data.ShouldBeOfType<WebhookTestData>();
    }

    [Fact]
    public void AC45_Recipients_changed_events_carry_the_revision()
    {
        var haEvent = HaEvent.RecipientsChanged("18", _time);

        haEvent.Type.ShouldBe("recipients.changed");
        haEvent.Data.ShouldBe(new RecipientsChangedData("18"));
        Should.Throw<ArgumentException>(() => HaEvent.RecipientsChanged(" ", _time));
    }

    [Fact]
    public void Create_validates_its_arguments()
    {
        Should.Throw<ArgumentException>(() => HaEvent.Create("", new WebhookTestData(), _time));
        Should.Throw<ArgumentNullException>(() => HaEvent.Create(HaEventTypes.DeliverySent, null!, _time));
        Should.Throw<ArgumentNullException>(() => HaEvent.Create(HaEventTypes.DeliveryFailed, new WebhookTestData(), null!));
    }

    [Fact]
    public void Send_results_and_outcomes_carry_the_status_and_error()
    {
        var delivered = WebhookSendResult.Delivered(200);
        delivered.Succeeded.ShouldBeTrue();
        delivered.StatusCode.ShouldBe(200);
        delivered.Error.ShouldBeNull();

        var failed = WebhookSendResult.Failed("boom", 500);
        failed.Succeeded.ShouldBeFalse();
        failed.StatusCode.ShouldBe(500);
        WebhookSendResult.Failed("timeout").StatusCode.ShouldBeNull();
        Should.Throw<ArgumentException>(() => WebhookSendResult.Failed(" "));

        HaEventOutcome.Delivered("evt_1", 204).ShouldBe(new HaEventOutcome(HaEventOutcomeKind.Delivered, "evt_1", 204, null));
        HaEventOutcome.Failed("evt_1", "boom", null).Kind.ShouldBe(HaEventOutcomeKind.Failed);
        HaEventOutcome.NotRegistered("evt_1").Kind.ShouldBe(HaEventOutcomeKind.NotRegistered);
    }

    [Fact]
    public void Webhook_targets_never_print_their_secret()
    {
        var target = new WebhookTarget(new Uri("http://homeassistant:8123/api/webhook/abc"), [1, 2, 3]);

        target.ToString().ShouldBe("WebhookTarget(homeassistant:8123, secret redacted)");
        Should.Throw<ArgumentNullException>(() => new WebhookTarget(null!, []));
        Should.Throw<ArgumentNullException>(() => new WebhookTarget(target.Url, null!));
    }
}
