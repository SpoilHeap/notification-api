using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.Channels;
using NotificationApi.Application.Notifications;
using NotificationApi.Application.Tests.Fakes;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Tests;

/// <summary>
/// The delivery use cases on in-memory fakes, with the spec §12 fixtures: Alice (Telegram + SMS; normal → telegram,
/// high → telegram, sms), Bob (Telegram; normal and high → telegram) and Carol (no endpoints).
/// </summary>
internal sealed class DeliveryHarness
{
    public const string AliceSms = "+353861234567";

    public DeliveryHarness()
    {
        var now = Time.GetUtcNow();
        Alice = Contact.Create(Handle.Contact("alice"), DisplayName.Create("Alice"), now);
        Alice.LinkTelegram("1001", now);
        Alice.SetSmsNumber(PhoneNumber.Parse(AliceSms), now);
        Alice.SetRouting(Priority.Normal, [ChannelType.Telegram]);
        Alice.SetRouting(Priority.High, [ChannelType.Telegram, ChannelType.Sms]);
        Bob = Contact.Create(Handle.Contact("bob"), DisplayName.Create("Bob"), now);
        Bob.LinkTelegram("1002", now);
        Bob.SetRouting(Priority.Normal, [ChannelType.Telegram]);
        Bob.SetRouting(Priority.High, [ChannelType.Telegram]);
        Carol = Contact.Create(Handle.Contact("carol"), DisplayName.Create("Carol"), now);
        Recipients.Contacts.AddRange([Alice, Bob, Carol]);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 27, 16, 15, 20, TimeSpan.Zero));

    public InMemoryRecipients Recipients { get; } = new();

    public InMemoryNotifications Store { get; } = new();

    public Journal Journal { get; } = new();

    public DeliveryWakeUp WakeUp { get; } = new();

    public FakeChannel Telegram { get; } = new(ChannelType.Telegram);

    public FakeChannel Sms { get; } = new(ChannelType.Sms);

    public DeliveryOptions Options { get; } = new();

    public ListLogger<AttemptDelivery> AttemptLog { get; } = new();

    public Contact Alice { get; }

    public Contact Bob { get; }

    public Contact Carol { get; }

    public IEnumerable<INotificationChannel> Channels { get; set; } = [];

    public SendNotification Send => new(Store, Recipients, Recipients, Journal, WakeUp, Time, NullLogger<SendNotification>.Instance);

    public ProcessDueDeliveries Process => new(Store, Journal, Time, NullLogger<ProcessDueDeliveries>.Instance);

    public AttemptDelivery Attempt => new(
        Store, Channels.Any() ? Channels : [Telegram, Sms], Journal, Journal, WakeUp, Microsoft.Extensions.Options.Options.Create(Options), Time, AttemptLog);

    public RecoverInterruptedDeliveries Recover => new(Store, Journal, Journal, Time, NullLogger<RecoverInterruptedDeliveries>.Instance);

    public Group Group(string name, params Contact[] members)
    {
        var group = Domain.Recipients.Group.Create(Handle.Group(name), DisplayName.Create(name), Time.GetUtcNow());
        foreach (var member in members)
        {
            group.AddMember(member);
        }

        Recipients.Groups.Add(group);
        return group;
    }

    public static SendNotificationCommand Command(
        string[] to,
        string priority = "normal",
        string? title = "Front door",
        string message = "Door opened",
        string[]? channels = null,
        string? key = null,
        string? tag = null,
        string? source = null) =>
        new(to, title, message, priority, channels, tag, source, key, HasActions: false);

    public async Task<NotificationReceipt> SendAsync(SendNotificationCommand command)
    {
        var result = await Send.HandleAsync(command, CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    /// <summary>One worker pass: claims what is due and attempts each claimed delivery.</summary>
    public async Task<DueDeliveries> RunWorkerOnceAsync()
    {
        var due = await Process.HandleAsync(CancellationToken.None);
        foreach (var id in due.Claimed)
        {
            await Attempt.HandleAsync(id, CancellationToken.None);
        }

        return due;
    }

    public Delivery Delivery(DeliveryId id) => Store.Deliveries.Single(d => d.Id == id);
}
