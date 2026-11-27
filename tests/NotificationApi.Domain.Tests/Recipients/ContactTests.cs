using System.Reflection;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Tests.Recipients;

public sealed class ContactTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static Contact Alice() => Contact.Create(Handle.Contact("alice"), DisplayName.Create("Alice"), Now);

    [Fact]
    public void A_new_contact_has_its_handle_and_name_and_nothing_else()
    {
        var alice = Alice();

        alice.Handle.ToString().ShouldBe("contact:alice");
        alice.Name.Value.ShouldBe("Alice");
        alice.CreatedAt.ShouldBe(Now);
        alice.Endpoints.ShouldBeEmpty();
        alice.SmsNumber.ShouldBeNull();
        alice.Routing.ShouldBe(ContactRouting.Empty);
        alice.Id.ShouldNotBe(Alice().Id);
    }

    [Fact]
    public void A_contact_needs_a_contact_handle() =>
        Should.Throw<ArgumentException>(() => Contact.Create(Handle.Group("alice"), DisplayName.Create("Alice"), Now));

    [Fact]
    public void AC51_The_handle_cannot_be_changed()
    {
        var handle = typeof(Contact).GetProperty(nameof(Contact.Handle))!;

        handle.SetMethod.ShouldBeNull("the handle is get-only, so no code path can change it");
        typeof(Contact).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(Handle)))
            .ShouldBeEmpty("no public method takes a new handle");
    }

    [Fact]
    public void AC51_Renaming_keeps_the_handle()
    {
        var alice = Alice();

        alice.Rename(DisplayName.Create("Alice Smith")).ShouldBeTrue();
        alice.Rename(DisplayName.Create("Alice Smith")).ShouldBeFalse();

        alice.Name.Value.ShouldBe("Alice Smith");
        alice.Handle.ToString().ShouldBe("contact:alice");
    }

    [Fact]
    public void An_sms_number_is_verified_when_entered()
    {
        var alice = Alice();

        alice.SetSmsNumber(PhoneNumber.Parse("+353861234567"), Now).ShouldBeTrue();

        var sms = alice.EndpointFor(ChannelType.Sms).ShouldNotBeNull();
        sms.Address.ShouldBe("+353861234567");
        sms.VerifiedAt.ShouldBe(Now);
        sms.IsVerified.ShouldBeTrue();
        sms.MaskedAddress.ShouldBe("+3538****567");
        sms.ToString().ShouldBe("Sms +3538****567");
        alice.SmsNumber.ShouldBe(PhoneNumber.Parse("+353861234567"));
    }

    [Fact]
    public void Changing_the_sms_number_replaces_the_endpoint()
    {
        var alice = Alice();
        alice.SetSmsNumber(PhoneNumber.Parse("+353861234567"), Now);

        alice.SetSmsNumber(PhoneNumber.Parse("+353861234567"), Now.AddDays(1)).ShouldBeFalse();
        alice.SetSmsNumber(PhoneNumber.Parse("+353879999999"), Now.AddDays(1)).ShouldBeTrue();

        var sms = alice.Endpoints.ShouldHaveSingleItem();
        sms.Address.ShouldBe("+353879999999");
        sms.VerifiedAt.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void AC53_Routing_can_use_channels_with_an_endpoint()
    {
        var alice = Alice();
        alice.SetSmsNumber(PhoneNumber.Parse("+353861234567"), Now);

        alice.SetRouting(Priority.High, [ChannelType.Sms]).ShouldBeTrue();
        alice.SetRouting(Priority.High, [ChannelType.Sms]).ShouldBeFalse();

        alice.Routing.ChannelsFor(Priority.High).ShouldBe([ChannelType.Sms]);
        alice.Routing.ChannelsFor(Priority.Normal).ShouldBeEmpty();
    }

    [Fact]
    public void AC53_Routing_cannot_name_a_channel_without_an_endpoint()
    {
        var alice = Alice();
        alice.SetSmsNumber(PhoneNumber.Parse("+353861234567"), Now);

        alice.ChannelsWithoutEndpoint([ChannelType.Telegram, ChannelType.Sms, ChannelType.Telegram]).ShouldBe([ChannelType.Telegram]);
        var error = Should.Throw<InvalidOperationException>(() => alice.SetRouting(Priority.High, [ChannelType.Sms, ChannelType.Telegram]));

        error.Message.ShouldContain("Telegram");
        alice.Routing.ShouldBe(ContactRouting.Empty);
    }

    [Fact]
    public void Routing_rejects_a_channel_twice_in_one_priority()
    {
        var alice = Alice();
        alice.SetSmsNumber(PhoneNumber.Parse("+353861234567"), Now);

        Should.Throw<ArgumentException>(() => alice.SetRouting(Priority.High, [ChannelType.Sms, ChannelType.Sms]));
    }

    [Fact]
    public void AC53_Removing_an_endpoint_removes_its_channel_from_routing()
    {
        var alice = Alice();
        alice.SetSmsNumber(PhoneNumber.Parse("+353861234567"), Now);
        alice.SetRouting(Priority.Normal, [ChannelType.Sms]);
        alice.SetRouting(Priority.High, [ChannelType.Sms]);

        alice.RemoveEndpoint(ChannelType.Sms).ShouldBeTrue();
        alice.RemoveEndpoint(ChannelType.Sms).ShouldBeFalse();

        alice.Endpoints.ShouldBeEmpty();
        alice.Routing.ShouldBe(ContactRouting.Empty);
    }
}
