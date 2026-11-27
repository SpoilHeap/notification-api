using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Notifications;

/// <summary>
/// One message to one contact over one channel, reached through one top-level handle (spec §5 step 3). A small state
/// machine (engineering_standards.md §2.2):
/// <code>
/// Queued ──► Sending ──► Sent
///    ▲          │
///    │          ├──► Retrying ──► Sending (again, when due)
///    │          └──► Failed
/// </code>
/// Illegal transitions throw. The endpoint address is copied when the delivery is created, so a delivery is
/// self-contained and later contact edits don't change what was queued.
/// </summary>
public sealed class Delivery
{
    public const int MaxErrorLength = 500;
    public const int MaxProviderMessageIdLength = 200;

    private Delivery(
        DeliveryId id,
        NotificationId notificationId,
        int position,
        Handle contact,
        Handle via,
        ChannelType channel,
        string address,
        DateTimeOffset createdAt)
    {
        Id = id;
        NotificationId = notificationId;
        Position = position;
        Contact = contact;
        Via = via;
        Channel = channel;
        Address = address;
        CreatedAt = createdAt;
        Status = DeliveryStatus.Queued;
        NextAttemptAt = createdAt;
    }

    public DeliveryId Id { get; private set; }

    public NotificationId NotificationId { get; private set; }

    /// <summary>The delivery's place in the notification's list (spec §4.1: handles in <c>to</c> order, depth-first, then channel order).</summary>
    public int Position { get; private set; }

    /// <summary>The contact's handle, e.g. <c>contact:alice</c>.</summary>
    public Handle Contact { get; private set; }

    /// <summary>The top-level handle from <c>to</c> that reached the contact, e.g. <c>group:family</c>.</summary>
    public Handle Via { get; private set; }

    public ChannelType Channel { get; private set; }

    /// <summary>The full address (E.164 number or chat id). Never log it: use <see cref="MaskedAddress"/>.</summary>
    public string Address { get; private set; }

    public string MaskedAddress => Channel == ChannelType.Sms && PhoneNumber.TryParse(Address, out var number) ? number.Masked : "(chat)";

    public DeliveryStatus Status { get; private set; }

    /// <summary>How many attempts have started.</summary>
    public int Attempts { get; private set; }

    /// <summary>Why the last attempt failed. Phone numbers in it are masked.</summary>
    public string? LastError { get; private set; }

    /// <summary>The provider's id for the sent message (the SMS.to <c>message_id</c>, AC22).</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>When the next attempt is due: set while <see cref="DeliveryStatus.Queued"/> or <see cref="DeliveryStatus.Retrying"/>.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>When it reached <see cref="DeliveryStatus.Sent"/> or <see cref="DeliveryStatus.Failed"/>.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsFinal => Status is DeliveryStatus.Sent or DeliveryStatus.Failed;

    public static Delivery Create(
        NotificationId notificationId,
        int position,
        Handle contact,
        Handle via,
        ChannelType channel,
        string address,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(via);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        if (contact.Type != HandleType.Contact)
        {
            throw new ArgumentException("A delivery goes to a contact: handle.", nameof(contact));
        }

        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown channel.");
        }

        return new Delivery(DeliveryId.New(now), notificationId, position, contact, via, channel, address, now);
    }

    public bool IsDue(DateTimeOffset now) =>
        Status is DeliveryStatus.Queued or DeliveryStatus.Retrying && NextAttemptAt <= now;

    /// <summary><c>Queued | Retrying → Sending</c>. Counts the attempt.</summary>
    public void StartAttempt(DateTimeOffset now)
    {
        Require(nameof(StartAttempt), DeliveryStatus.Queued, DeliveryStatus.Retrying);
        Status = DeliveryStatus.Sending;
        Attempts++;
        LastAttemptAt = now;
        NextAttemptAt = null;
    }

    /// <summary><c>Sending → Sent</c>. Final.</summary>
    public void MarkSent(string? providerMessageId, DateTimeOffset now)
    {
        Require(nameof(MarkSent), DeliveryStatus.Sending);
        Status = DeliveryStatus.Sent;
        ProviderMessageId = providerMessageId is null ? null : Truncate(providerMessageId, MaxProviderMessageIdLength);
        CompletedAt = now;
    }

    /// <summary>
    /// <c>Sending → Retrying</c> while the policy allows another attempt (due <see cref="RetryPolicy.Interval"/> from
    /// <paramref name="now"/>), otherwise <c>Sending → Failed</c>.
    /// </summary>
    public void RecordFailedAttempt(string error, DateTimeOffset now, RetryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Require(nameof(RecordFailedAttempt), DeliveryStatus.Sending);
        if (Attempts >= policy.MaxAttempts)
        {
            Fail(error, now);
            return;
        }

        LastError = CleanError(error);
        Status = DeliveryStatus.Retrying;
        NextAttemptAt = now + policy.Interval;
    }

    /// <summary><c>Sending → Failed</c> with no retry: the delivery can't succeed (e.g. no channel) or its outcome is unknown.</summary>
    public void Fail(string error, DateTimeOffset now)
    {
        Require(nameof(Fail), DeliveryStatus.Sending);
        LastError = CleanError(error);
        Status = DeliveryStatus.Failed;
        NextAttemptAt = null;
        CompletedAt = now;
    }

    private void Require(string transition, params DeliveryStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOperationException($"Delivery {Id} can't {transition} while {Status}.");
        }
    }

    private static string CleanError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return Truncate(PhoneNumber.MaskAll(error.Trim()), MaxErrorLength);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
