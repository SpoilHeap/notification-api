using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;
using NotificationApi.Infrastructure.Persistence.Configurations;

namespace NotificationApi.Infrastructure.Tests.Persistence;

public sealed class RoutingConverterTests
{
    [Fact]
    public void Round_trips_routing_in_order()
    {
        var routing = ContactRouting.Empty
            .With(Priority.High, [ChannelType.Telegram, ChannelType.Sms])
            .With(Priority.Normal, [ChannelType.Telegram]);

        var text = RoutingConverter.Format(routing);

        text.ShouldBe("normal=telegram;high=telegram,sms");
        RoutingConverter.Parse(text).ShouldBe(routing);
        RoutingConverter.Parse(string.Empty).ShouldBe(ContactRouting.Empty);
        RoutingConverter.Parse("low=").ShouldBe(ContactRouting.Empty);
        RoutingConverter.Comparer.Equals(routing, RoutingConverter.Parse(text)).ShouldBeTrue();
        RoutingConverter.Comparer.GetHashCode(routing).ShouldBe(routing.GetHashCode());
        RoutingConverter.Comparer.Snapshot(routing).ShouldBeSameAs(routing);
    }
}
