using NotificationApi.Application.Common;
using NotificationApi.Application.Notifications;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Tests.Notifications;

/// <summary><see cref="SendNotification"/>: expansion, channel selection, all-or-nothing and idempotency (spec §4.1, §5).</summary>
public sealed class SendNotificationTests
{
    private readonly DeliveryHarness _h = new();

    [Fact]
    public async Task AC01_Normal_priority_to_alice_queues_one_telegram_delivery()
    {
        var receipt = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"]));

        receipt.NotificationId.ToString().ShouldStartWith("ntf_");
        var delivery = receipt.Deliveries.ShouldHaveSingleItem();
        (delivery.Contact, delivery.Via, delivery.Channel, delivery.Status).ShouldBe(("alice", "contact:alice", "telegram", "queued"));
        delivery.DeliveryId.ToString().ShouldStartWith("dlv_");
        _h.Store.Deliveries.ShouldHaveSingleItem().Status.ShouldBe(DeliveryStatus.Queued);
    }

    [Fact]
    public async Task AC02_High_priority_queues_telegram_then_sms()
    {
        var receipt = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], "high"));

        receipt.Deliveries.Select(d => d.Channel).ShouldBe(["telegram", "sms"]);
        _h.Store.Deliveries.Single(d => d.Channel == ChannelType.Sms).Address.ShouldBe(DeliveryHarness.AliceSms);
    }

    [Fact]
    public async Task AC03_AC04_AC05_Groups_expand_depth_first_without_deduplication_in_response_order()
    {
        var kids = _h.Group("kids", _h.Alice);
        var family = _h.Group("family");
        family.AddMember(kids, new Domain.Recipients.GroupGraph(_h.Recipients.Groups));
        family.AddMember(_h.Alice);
        _h.Group("security", _h.Alice, _h.Bob);

        var receipt = await _h.SendAsync(DeliveryHarness.Command(["group:family", "group:security", "contact:alice"]));

        receipt.Deliveries.Select(d => $"{d.Contact}/{d.Via}").ShouldBe(
            ["alice/group:family", "alice/group:family", "alice/group:security", "bob/group:security", "alice/contact:alice"]);
        _h.Store.Deliveries.Select(d => d.Position).ShouldBe([0, 1, 2, 3, 4]);
    }

    [Fact]
    public async Task AC06_An_sms_override_reaches_only_contacts_with_a_verified_number()
    {
        var receipt = await _h.SendAsync(DeliveryHarness.Command(["contact:alice", "contact:bob"], channels: ["sms"]));

        var only = receipt.Deliveries.ShouldHaveSingleItem();
        (only.Contact, only.Channel).ShouldBe(("alice", "sms"));
        _h.Store.Notifications.ShouldHaveSingleItem().ChannelOverride.ShouldBe([ChannelType.Sms]);
    }

    [Fact]
    public async Task AC08_Zero_deliveries_is_a_no_deliveries_error_and_saves_nothing()
    {
        var empty = _h.Group("empty");

        var carol = await _h.Send.HandleAsync(DeliveryHarness.Command(["contact:carol"]), CancellationToken.None);
        var nobody = await _h.Send.HandleAsync(DeliveryHarness.Command([empty.Handle.ToString()]), CancellationToken.None);

        carol.Error.Code.ShouldBe(NotificationErrors.NoDeliveriesCode);
        carol.Error.Message.ShouldBe("None of the 1 resolved contact(s) has a verified endpoint for the selected channels.");
        nobody.Error.Message.ShouldContain("no contacts");
        _h.Store.Notifications.ShouldBeEmpty();
        _h.Journal.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task AC09_Unknown_handles_are_all_named_and_nothing_is_queued_for_anyone()
    {
        var result = await _h.Send.HandleAsync(
            DeliveryHarness.Command(["contact:alice", "contact:zed", "group:nope", "contact:zed"]), CancellationToken.None);

        result.Error.Code.ShouldBe(Error.ValidationCode);
        result.Error.FieldErrors[NotifyFields.To].ShouldBe(["Unknown handle: contact:zed", "Unknown handle: group:nope"]);
        _h.Store.Notifications.ShouldBeEmpty();
        _h.Store.Deliveries.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC10_An_invalid_payload_saves_nothing()
    {
        var result = await _h.Send.HandleAsync(DeliveryHarness.Command(["contact:alice"], "High"), CancellationToken.None);

        result.Error.FieldErrors.Keys.ShouldBe([NotifyFields.Priority]);
        _h.Store.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Accepting_commits_once_and_wakes_the_worker()
    {
        await _h.SendAsync(DeliveryHarness.Command(["contact:alice"]));

        _h.Journal.Steps.ShouldBe(["save"]);
        var woken = _h.WakeUp.WaitAsync(TimeSpan.FromHours(1), _h.Time, CancellationToken.None);
        woken.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task AC12_The_same_key_within_24_hours_returns_the_original_receipt_whatever_the_payload()
    {
        var first = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], "high", key: "K"));
        _h.Time.Advance(TimeSpan.FromHours(23));

        var again = await _h.SendAsync(DeliveryHarness.Command(["contact:bob"], "low", key: "K"));
        var invalid = await _h.Send.HandleAsync(new SendNotificationCommand(null, null, null, null, null, null, null, "K", true), CancellationToken.None);

        ShouldBeSame(again, first);
        again.Deliveries.ShouldAllBe(d => d.Status == "queued");
        ShouldBeSame(invalid.Value, first);
        _h.Store.Notifications.Count.ShouldBe(1);
        _h.Store.Deliveries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AC12_The_replayed_receipt_still_says_queued_after_delivery()
    {
        var first = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], key: "K"));
        await _h.RunWorkerOnceAsync();
        _h.Store.Deliveries.ShouldHaveSingleItem().Status.ShouldBe(DeliveryStatus.Sent);

        ShouldBeSame(await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], key: "K")), first);
    }

    [Fact]
    public async Task AC12_After_24_hours_the_key_is_new()
    {
        var first = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], key: "K"));
        _h.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMilliseconds(1));

        var second = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], key: "K"));

        second.NotificationId.ShouldNotBe(first.NotificationId);
        _h.Store.Notifications.Count.ShouldBe(2);
        _h.Time.Advance(TimeSpan.FromHours(1));
        ShouldBeSame(await _h.SendAsync(DeliveryHarness.Command(["contact:bob"], key: "K")), second); // the newest match wins
    }

    [Fact]
    public async Task AC12_Rejected_requests_never_record_a_key()
    {
        (await _h.Send.HandleAsync(DeliveryHarness.Command(["contact:zed"], key: "K"), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await _h.Send.HandleAsync(DeliveryHarness.Command(["contact:carol"], key: "K"), CancellationToken.None)).IsFailure.ShouldBeTrue();

        var accepted = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], key: "K"));

        accepted.Deliveries.ShouldHaveSingleItem();
        _h.Store.Notifications.ShouldHaveSingleItem().IdempotencyKey.ShouldBe("K");
    }

    [Fact]
    public async Task Different_keys_are_independent()
    {
        var a = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], key: "A"));
        var b = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], key: "B"));

        b.NotificationId.ShouldNotBe(a.NotificationId);
    }

    private static void ShouldBeSame(NotificationReceipt actual, NotificationReceipt expected)
    {
        actual.NotificationId.ShouldBe(expected.NotificationId);
        actual.Deliveries.ShouldBe(expected.Deliveries);
    }
}
