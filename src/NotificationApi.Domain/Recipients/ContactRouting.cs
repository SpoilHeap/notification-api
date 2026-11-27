using NotificationApi.Domain.Notifications;

namespace NotificationApi.Domain.Recipients;

/// <summary>
/// A contact's channels for each priority, in order and without duplicates (spec §5 step 2). Immutable: every change
/// returns a new value. A priority with no entry routes to no channel.
/// </summary>
public sealed class ContactRouting : IEquatable<ContactRouting>
{
    public static readonly ContactRouting Empty = new(new Dictionary<Priority, IReadOnlyList<ChannelType>>());

    private readonly Dictionary<Priority, IReadOnlyList<ChannelType>> _byPriority;

    private ContactRouting(Dictionary<Priority, IReadOnlyList<ChannelType>> byPriority) => _byPriority = byPriority;

    /// <summary>Every priority that routes to at least one channel.</summary>
    public IReadOnlyDictionary<Priority, IReadOnlyList<ChannelType>> ByPriority => _byPriority;

    public static ContactRouting Create(IEnumerable<KeyValuePair<Priority, IReadOnlyList<ChannelType>>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var routing = Empty;
        foreach (var (priority, channels) in entries)
        {
            routing = routing.With(priority, channels);
        }

        return routing;
    }

    public IReadOnlyList<ChannelType> ChannelsFor(Priority priority) =>
        _byPriority.TryGetValue(priority, out var channels) ? channels : [];

    public ContactRouting With(Priority priority, IReadOnlyList<ChannelType> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority.");
        }

        if (channels.Any(c => !Enum.IsDefined(c)))
        {
            throw new ArgumentOutOfRangeException(nameof(channels), "Unknown channel.");
        }

        if (channels.Distinct().Count() != channels.Count)
        {
            throw new ArgumentException("A channel can appear only once per priority.", nameof(channels));
        }

        var next = new Dictionary<Priority, IReadOnlyList<ChannelType>>(_byPriority);
        if (channels.Count == 0)
        {
            next.Remove(priority);
        }
        else
        {
            next[priority] = [.. channels];
        }

        return new ContactRouting(next);
    }

    /// <summary>The same routing with <paramref name="channel"/> removed from every priority.</summary>
    public ContactRouting Without(ChannelType channel) =>
        Create(_byPriority.Select(p => KeyValuePair.Create(p.Key, (IReadOnlyList<ChannelType>)[.. p.Value.Where(c => c != channel)])));

    public bool Equals(ContactRouting? other) =>
        other is not null
        && _byPriority.Count == other._byPriority.Count
        && _byPriority.All(p => other._byPriority.TryGetValue(p.Key, out var channels) && channels.SequenceEqual(p.Value));

    public override bool Equals(object? obj) => Equals(obj as ContactRouting);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var (priority, channels) in _byPriority.OrderBy(p => p.Key))
        {
            hash.Add(priority);
            foreach (var channel in channels)
            {
                hash.Add(channel);
            }
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        string.Join("; ", Enum.GetValues<Priority>().Select(p => $"{p}: {(ChannelsFor(p) is { Count: > 0 } c ? string.Join(", ", c) : "none")}"));
}
