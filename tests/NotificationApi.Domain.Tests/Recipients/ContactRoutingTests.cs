using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Tests.Recipients;

public sealed class ContactRoutingTests
{
    [Fact]
    public void Keeps_the_channel_order_per_priority()
    {
        var routing = ContactRouting.Empty
            .With(Priority.High, [ChannelType.Sms, ChannelType.Telegram])
            .With(Priority.Normal, [ChannelType.Telegram]);

        routing.ChannelsFor(Priority.High).ShouldBe([ChannelType.Sms, ChannelType.Telegram]);
        routing.ChannelsFor(Priority.Normal).ShouldBe([ChannelType.Telegram]);
        routing.ChannelsFor(Priority.Low).ShouldBeEmpty();
        routing.ByPriority.Keys.ShouldBe([Priority.High, Priority.Normal], ignoreOrder: true);
    }

    [Fact]
    public void Is_immutable_and_compared_by_value()
    {
        var empty = ContactRouting.Empty;
        var high = empty.With(Priority.High, [ChannelType.Sms]);

        empty.ChannelsFor(Priority.High).ShouldBeEmpty();
        high.ShouldBe(ContactRouting.Create([KeyValuePair.Create(Priority.High, (IReadOnlyList<ChannelType>)[ChannelType.Sms])]));
        high.GetHashCode().ShouldBe(ContactRouting.Empty.With(Priority.High, [ChannelType.Sms]).GetHashCode());
        high.ShouldNotBe(empty.With(Priority.High, [ChannelType.Telegram]));
        high.ShouldNotBe(empty.With(Priority.High, [ChannelType.Sms]).With(Priority.Low, [ChannelType.Sms]));
        high.Equals((object?)null).ShouldBeFalse();
        high.With(Priority.High, []).ShouldBe(empty);
    }

    [Fact]
    public void Order_matters_for_equality() =>
        ContactRouting.Empty.With(Priority.High, [ChannelType.Sms, ChannelType.Telegram])
            .ShouldNotBe(ContactRouting.Empty.With(Priority.High, [ChannelType.Telegram, ChannelType.Sms]));

    [Fact]
    public void Without_removes_a_channel_everywhere()
    {
        var routing = ContactRouting.Empty
            .With(Priority.High, [ChannelType.Sms, ChannelType.Telegram])
            .With(Priority.Normal, [ChannelType.Sms]);

        var withoutSms = routing.Without(ChannelType.Sms);

        withoutSms.ChannelsFor(Priority.High).ShouldBe([ChannelType.Telegram]);
        withoutSms.ByPriority.ContainsKey(Priority.Normal).ShouldBeFalse();
    }

    [Fact]
    public void Rejects_duplicates_and_unknown_values()
    {
        Should.Throw<ArgumentException>(() => ContactRouting.Empty.With(Priority.High, [ChannelType.Sms, ChannelType.Sms]));
        Should.Throw<ArgumentOutOfRangeException>(() => ContactRouting.Empty.With((Priority)42, [ChannelType.Sms]));
        Should.Throw<ArgumentOutOfRangeException>(() => ContactRouting.Empty.With(Priority.High, [(ChannelType)42]));
    }

    [Fact]
    public void Describes_every_priority() =>
        ContactRouting.Empty.With(Priority.High, [ChannelType.Telegram, ChannelType.Sms]).ToString()
            .ShouldBe("Low: none; Normal: none; High: Telegram, Sms; Critical: none");
}
