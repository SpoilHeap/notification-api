using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Notifications;

/// <summary>One delivery to create: a contact, the handle that reached it, a channel and the endpoint to use.</summary>
public sealed record PlannedDelivery(Contact Contact, Handle Via, ChannelType Channel, Endpoint Endpoint);

/// <summary>
/// Picks the channels for each expanded recipient (spec §5 step 2): the <c>channels</c> override if there is one,
/// otherwise the contact's routing for the priority, kept in that order and limited to the contact's
/// <b>verified</b> endpoints (AC06, AC07). Pure.
/// </summary>
public static class DeliveryPlanner
{
    public static IReadOnlyList<PlannedDelivery> Plan(
        IEnumerable<ExpandedRecipient> recipients, Priority priority, IReadOnlyList<ChannelType> channelOverride)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(channelOverride);

        var plan = new List<PlannedDelivery>();
        foreach (var (contact, via) in recipients)
        {
            var channels = channelOverride.Count > 0 ? channelOverride.Distinct() : contact.Routing.ChannelsFor(priority);
            foreach (var channel in channels)
            {
                if (contact.EndpointFor(channel) is { IsVerified: true } endpoint)
                {
                    plan.Add(new PlannedDelivery(contact, via, channel, endpoint));
                }
            }
        }

        return plan;
    }
}
