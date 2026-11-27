using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Tests.Notifications;

/// <summary>Channel selection (spec §5 step 2): override or routing, limited to verified endpoints, in order.</summary>
public sealed class DeliveryPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 16, 0, 0, TimeSpan.Zero);

    private readonly Contact _alice = Fixtures.Alice(Now);
    private readonly Contact _bob = Fixtures.Bob(Now);
    private readonly Contact _carol = Contact.Create(Handle.Contact("carol"), DisplayName.Create("Carol"), Now);

    [Theory]
    [InlineData(Priority.Normal, "telegram")]
    [InlineData(Priority.High, "telegram,sms")]
    [InlineData(Priority.Low, "")]
    public void AC01_AC02_Routing_for_the_priority_decides_in_order(Priority priority, string expected)
    {
        Channels(DeliveryPlanner.Plan([Via(_alice)], priority, [])).ShouldBe(expected);
    }

    [Fact]
    public void AC06_An_override_replaces_routing_but_only_reaches_verified_endpoints()
    {
        var plan = DeliveryPlanner.Plan([Via(_alice), Via(_bob)], Priority.Normal, [ChannelType.Sms]);

        plan.ShouldHaveSingleItem().Contact.ShouldBe(_alice);
        plan[0].Channel.ShouldBe(ChannelType.Sms);
        plan[0].Endpoint.Address.ShouldBe("+353861234567");
    }

    [Fact]
    public void The_override_order_is_kept_and_duplicates_dropped()
    {
        Channels(DeliveryPlanner.Plan([Via(_alice)], Priority.Low, [ChannelType.Sms, ChannelType.Telegram, ChannelType.Sms]))
            .ShouldBe("sms,telegram");
    }

    [Fact]
    public void AC07_A_routed_channel_with_an_unverified_endpoint_is_skipped()
    {
        Fixtures.Unverify(_alice.EndpointFor(ChannelType.Sms)!);

        Channels(DeliveryPlanner.Plan([Via(_alice)], Priority.High, [])).ShouldBe("telegram");
        Channels(DeliveryPlanner.Plan([Via(_alice)], Priority.High, [ChannelType.Sms])).ShouldBe("");
    }

    [Fact]
    public void AC08_A_contact_with_no_endpoints_yields_nothing()
    {
        DeliveryPlanner.Plan([Via(_carol)], Priority.Critical, [ChannelType.Sms, ChannelType.Telegram]).ShouldBeEmpty();
    }

    [Fact]
    public void Duplicated_recipients_each_get_their_deliveries()
    {
        var plan = DeliveryPlanner.Plan([Via(_alice, "group:family"), Via(_alice, "contact:alice")], Priority.High, []);

        plan.Select(p => $"{p.Via}/{p.Channel}").ShouldBe(["group:family/Telegram", "group:family/Sms", "contact:alice/Telegram", "contact:alice/Sms"]);
    }

    [Fact]
    public void Deliveries_are_numbered_in_plan_order_and_copy_the_address()
    {
        var notification = Notification.Create("t", "m", Priority.High, [], null, null, null, Now);

        var deliveries = notification.CreateDeliveries(DeliveryPlanner.Plan([Via(_alice)], Priority.High, []), Now);

        deliveries.Select(d => (d.Position, d.Channel, d.Address)).ShouldBe([(0, ChannelType.Telegram, "1001"), (1, ChannelType.Sms, "+353861234567")]);
        deliveries.ShouldAllBe(d => d.NotificationId == notification.Id && d.Contact == _alice.Handle && d.Via == _alice.Handle);
    }

    private static ExpandedRecipient Via(Contact contact, string? via = null) => new(contact, via is null ? contact.Handle : Handle.Parse(via));

    private static string Channels(IEnumerable<PlannedDelivery> plan) =>
        string.Join(',', plan.Select(p => p.Channel.ToString().ToLowerInvariant()));
}

/// <summary>The spec §12 fixtures: Alice (Telegram + SMS), Bob (Telegram only).</summary>
internal static class Fixtures
{
    public static Contact Alice(DateTimeOffset now)
    {
        var alice = Contact.Create(Handle.Contact("alice"), DisplayName.Create("Alice"), now);
        alice.LinkTelegram("1001", now);
        alice.SetSmsNumber(PhoneNumber.Parse("+353861234567"), now);
        alice.SetRouting(Priority.Normal, [ChannelType.Telegram]);
        alice.SetRouting(Priority.High, [ChannelType.Telegram, ChannelType.Sms]);
        return alice;
    }

    public static Contact Bob(DateTimeOffset now)
    {
        var bob = Contact.Create(Handle.Contact("bob"), DisplayName.Create("Bob"), now);
        bob.LinkTelegram("1002", now);
        bob.SetRouting(Priority.Normal, [ChannelType.Telegram]);
        bob.SetRouting(Priority.High, [ChannelType.Telegram]);
        return bob;
    }

    /// <summary>No domain operation makes an endpoint unverified yet (SMS is verified on entry, Telegram by linking).</summary>
    public static void Unverify(Endpoint endpoint) =>
        typeof(Endpoint).GetProperty(nameof(Endpoint.VerifiedAt))!.SetValue(endpoint, null);
}
