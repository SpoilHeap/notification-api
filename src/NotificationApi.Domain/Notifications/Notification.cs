namespace NotificationApi.Domain.Notifications;

/// <summary>
/// One accepted <c>POST /notify</c> request (spec §4.1, §7): what HA asked to send. Its deliveries are separate
/// <see cref="Delivery"/> rows, so each one is processed on its own (AC26). Immutable once created.
/// </summary>
public sealed class Notification
{
    public const int MaxTitleLength = 200;
    public const int MinMessageLength = 1;
    public const int MaxMessageLength = 4000;

    /// <summary>The limit for <c>tag</c>, <c>source</c> and <c>idempotency_key</c>.</summary>
    public const int MaxTextLength = 200;

    private Notification(
        NotificationId id,
        string? title,
        string message,
        Priority priority,
        IReadOnlyList<ChannelType> channelOverride,
        string? tag,
        string? source,
        string? idempotencyKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        Title = title;
        Message = message;
        Priority = priority;
        ChannelOverride = channelOverride;
        Tag = tag;
        Source = source;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }

    public NotificationId Id { get; private set; }

    public string? Title { get; private set; }

    /// <summary>The body as HA sent it. Never log it at Information or above (engineering_standards.md §5).</summary>
    public string Message { get; private set; }

    public Priority Priority { get; private set; }

    /// <summary>HA's <c>channels</c> override, or empty when the contacts' routing decides.</summary>
    public IReadOnlyList<ChannelType> ChannelOverride { get; private set; }

    public string? Tag { get; private set; }

    public string? Source { get; private set; }

    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification Create(
        string? title,
        string message,
        Priority priority,
        IReadOnlyList<ChannelType> channelOverride,
        string? tag,
        string? source,
        string? idempotencyKey,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(channelOverride);
        if (message.Length is < MinMessageLength or > MaxMessageLength)
        {
            throw new ArgumentException($"The message must be {MinMessageLength}-{MaxMessageLength} characters.", nameof(message));
        }

        CheckLength(title, MaxTitleLength, nameof(title));
        CheckLength(tag, MaxTextLength, nameof(tag));
        CheckLength(source, MaxTextLength, nameof(source));
        CheckLength(idempotencyKey, MaxTextLength, nameof(idempotencyKey));
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority.");
        }

        return new Notification(
            NotificationId.New(now), title, message, priority, [.. channelOverride.Distinct()], tag, source, idempotencyKey, now);
    }

    /// <summary>One delivery per planned (contact, via, channel), numbered in plan order (spec §4.1 "202 body").</summary>
    public IReadOnlyList<Delivery> CreateDeliveries(IReadOnlyList<PlannedDelivery> plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return [.. plan.Select((p, i) => Delivery.Create(Id, i, p.Contact.Handle, p.Via, p.Channel, p.Endpoint.Address, now))];
    }

    private static void CheckLength(string? value, int max, string name)
    {
        if (value is not null && value.Length > max)
        {
            throw new ArgumentException($"At most {max} characters.", name);
        }
    }
}
