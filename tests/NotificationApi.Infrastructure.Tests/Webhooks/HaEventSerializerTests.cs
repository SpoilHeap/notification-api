using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.Events;
using NotificationApi.Infrastructure.Webhooks;

namespace NotificationApi.Infrastructure.Tests.Webhooks;

public sealed class HaEventSerializerTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 17, 15, 20, 123, TimeSpan.FromHours(1)));

    [Fact]
    public void A_test_event_is_the_snake_case_envelope_with_empty_data()
    {
        var haEvent = HaEvent.WebhookTest(_time);

        var json = Encoding.UTF8.GetString(HaEventSerializer.Serialize(haEvent));

        json.ShouldBe($$$"""{"id":"{{{haEvent.Id}}}","type":"webhook.test","occurred_at":"2026-11-27T16:15:20Z","data":{}}""");
    }

    [Fact]
    public void AC45_Recipients_changed_carries_the_revision_in_data()
    {
        var haEvent = HaEvent.RecipientsChanged("18", _time);

        using var json = JsonDocument.Parse(HaEventSerializer.Serialize(haEvent));

        json.RootElement.GetProperty("type").GetString().ShouldBe("recipients.changed");
        json.RootElement.GetProperty("data").ToString().ShouldBe("""{"revision":"18"}""");
    }

    [Fact]
    public void Multi_word_data_fields_are_snake_case()
    {
        var haEvent = HaEvent.Create(HaEventTypes.DeliverySent, new SampleDeliveryData("ntf_1", "dlv_1"), _time);

        using var json = JsonDocument.Parse(HaEventSerializer.Serialize(haEvent));

        json.RootElement.GetProperty("data").ToString().ShouldBe("""{"notification_id":"ntf_1","delivery_id":"dlv_1"}""");
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["id", "type", "occurred_at", "data"]);
    }

    [Fact]
    public void Requires_an_event() => Should.Throw<ArgumentNullException>(() => HaEventSerializer.Serialize(null!));

    private sealed record SampleDeliveryData(string NotificationId, string DeliveryId) : HaEventData;
}
