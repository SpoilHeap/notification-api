using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Contacts;

/// <summary>An endpoint as the UI shows it: the address is always masked.</summary>
public sealed record EndpointView(ChannelType Channel, string MaskedAddress, DateTimeOffset? VerifiedAt)
{
    internal static EndpointView From(Endpoint endpoint) => new(endpoint.Channel, endpoint.MaskedAddress, endpoint.VerifiedAt);
}

/// <summary>A row of the Contacts list.</summary>
public sealed record ContactSummary(string Handle, string HandleName, string Name, IReadOnlyList<EndpointView> Endpoints);

/// <summary>The contact edit page.</summary>
/// <param name="Handle">E.g. <c>contact:alice</c>.</param>
/// <param name="HandleName">E.g. <c>alice</c>, used in the page URL.</param>
/// <param name="Name">The display name.</param>
/// <param name="Endpoints">Masked endpoints, one per channel at most.</param>
/// <param name="Routing">The channels for each priority; every priority is present.</param>
/// <param name="MemberOf">The groups that contain this contact directly (listed before deleting, AC56).</param>
public sealed record ContactDetails(
    string Handle,
    string HandleName,
    string Name,
    IReadOnlyList<EndpointView> Endpoints,
    IReadOnlyDictionary<Priority, IReadOnlyList<ChannelType>> Routing,
    IReadOnlyList<RecipientRef> MemberOf);
