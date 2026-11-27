namespace NotificationApi.Application.Events;

/// <summary>Event types POSTed to the HA webhook (spec §4.4).</summary>
public static class HaEventTypes
{
    /// <summary>Sent on demand from the Integration page, to check the webhook before any contacts exist.</summary>
    public const string WebhookTest = "webhook.test";

    public const string RecipientsChanged = "recipients.changed";

    public const string DeliverySent = "delivery.sent";

    public const string DeliveryFailed = "delivery.failed";
}

/// <summary>The <c>data</c> object of an event. Each type has its own record; it is serialised with snake_case names.</summary>
public abstract record HaEventData;

/// <summary><c>webhook.test</c>: <c>data: {}</c>.</summary>
public sealed record WebhookTestData : HaEventData;

/// <summary><c>recipients.changed</c>: <c>data: {"revision": "18"}</c>, the revision the next <c>GET /recipients</c> returns (AC45).</summary>
public sealed record RecipientsChangedData(string Revision) : HaEventData;

/// <summary>
/// The envelope <c>{"id", "type", "occurred_at", "data"}</c> (spec §4.4). The id is <c>evt_</c> plus 32 lower-case hex
/// digits of a time-ordered UUID (version 7), unique per event, so the receiver can de-duplicate on it.
/// </summary>
public sealed record HaEvent
{
    public const string IdPrefix = "evt_";

    private HaEvent(string id, string type, DateTimeOffset occurredAt, HaEventData data)
    {
        Id = id;
        Type = type;
        OccurredAt = occurredAt;
        Data = data;
    }

    public string Id { get; }

    public string Type { get; }

    /// <summary>When it happened, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; }

    public HaEventData Data { get; }

    public static HaEvent WebhookTest(TimeProvider time) => Create(HaEventTypes.WebhookTest, new WebhookTestData(), time);

    public static HaEvent RecipientsChanged(string revision, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        return Create(HaEventTypes.RecipientsChanged, new RecipientsChangedData(revision), time);
    }

    /// <summary>For event types added later (e.g. <c>delivery.*</c>).</summary>
    public static HaEvent Create(string type, HaEventData data, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(time);

        var now = time.GetUtcNow().ToUniversalTime();
        return new HaEvent(IdPrefix + Guid.CreateVersion7(now).ToString("N"), type, now, data);
    }
}
