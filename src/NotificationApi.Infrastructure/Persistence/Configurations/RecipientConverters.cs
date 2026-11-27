using System.Globalization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Infrastructure.Persistence.Configurations;

internal sealed class ContactIdConverter() : ValueConverter<ContactId, Guid>(id => id.Value, value => new ContactId(value));

internal sealed class GroupIdConverter() : ValueConverter<GroupId, Guid>(id => id.Value, value => new GroupId(value));

/// <summary>Stored as the full text, e.g. <c>contact:alice</c>, so a row reads the same as the API.</summary>
internal sealed class HandleConverter() : ValueConverter<Handle, string>(handle => handle.ToString(), value => Handle.Parse(value))
{
    /// <summary><c>group:</c> plus the longest name.</summary>
    public const int MaxLength = 6 + Handle.MaxNameLength;
}

internal sealed class DisplayNameConverter() : ValueConverter<DisplayName, string>(name => name.Value, value => DisplayName.Create(value));

/// <summary>
/// Routing as one text column: <c>normal=telegram,sms;high=sms</c> (lower-case, priorities in enum order, channels in
/// routing order). Empty when nothing is routed.
/// </summary>
internal sealed class RoutingConverter() : ValueConverter<ContactRouting, string>(routing => Format(routing), value => Parse(value))
{
    public const int MaxLength = 200;

    public static readonly ValueComparer<ContactRouting> Comparer = new(
        (a, b) => Equals(a, b),
        routing => routing.GetHashCode(),
        routing => routing); // immutable: a snapshot can share the instance

    public static string Format(ContactRouting routing) =>
        string.Join(';', routing.ByPriority
            .OrderBy(p => p.Key)
            .Select(p => $"{Name(p.Key)}={string.Join(',', p.Value.Select(Name))}"));

    public static ContactRouting Parse(string value)
    {
        var entries = new List<KeyValuePair<Priority, IReadOnlyList<ChannelType>>>();
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var (priority, channels) = (part.Split('=', 2)[0], part.Split('=', 2).ElementAtOrDefault(1) ?? string.Empty);
            entries.Add(KeyValuePair.Create(
                Enum.Parse<Priority>(priority, ignoreCase: true),
                (IReadOnlyList<ChannelType>)[.. channels.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => Enum.Parse<ChannelType>(c, ignoreCase: true))]));
        }

        return ContactRouting.Create(entries);
    }

    private static string Name<T>(T value)
        where T : struct, Enum => value.ToString().ToLower(CultureInfo.InvariantCulture);
}
