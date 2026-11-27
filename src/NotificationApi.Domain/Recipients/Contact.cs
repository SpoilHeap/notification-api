using NotificationApi.Domain.Notifications;

namespace NotificationApi.Domain.Recipients;

/// <summary>
/// A person who receives notifications. The <see cref="Handle"/> is fixed at creation (AC51); the name, endpoints and
/// routing can change. Routing may only name channels the contact has an endpoint for (AC53).
/// </summary>
public sealed class Contact
{
    private readonly List<Endpoint> _endpoints = [];

    private Contact(ContactId id, Handle handle, DisplayName name, DateTimeOffset createdAt)
    {
        Id = id;
        Handle = handle;
        Name = name;
        CreatedAt = createdAt;
        Routing = ContactRouting.Empty;
    }

    public ContactId Id { get; private set; }

    /// <summary><c>contact:&lt;name&gt;</c>. Get-only: HA automations depend on it, so it never changes (AC51).</summary>
    public Handle Handle { get; }

    public DisplayName Name { get; private set; }

    public IReadOnlyCollection<Endpoint> Endpoints => _endpoints;

    public ContactRouting Routing { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The SMS number, if the contact has one.</summary>
    public PhoneNumber? SmsNumber => EndpointFor(ChannelType.Sms) is { } sms ? PhoneNumber.Parse(sms.Address) : null;

    public static Contact Create(Handle handle, DisplayName name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(name);
        if (handle.Type != HandleType.Contact)
        {
            throw new ArgumentException("A contact needs a contact: handle.", nameof(handle));
        }

        return new Contact(ContactId.New(), handle, name, now);
    }

    /// <summary>Returns false when the name is unchanged.</summary>
    public bool Rename(DisplayName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name == Name)
        {
            return false;
        }

        Name = name;
        return true;
    }

    public Endpoint? EndpointFor(ChannelType channel) => _endpoints.SingleOrDefault(e => e.Channel == channel);

    /// <summary>
    /// Adds or replaces the SMS endpoint. An admin types the number, so it is verified on entry (spec §6).
    /// Returns false when the number is unchanged.
    /// </summary>
    public bool SetSmsNumber(PhoneNumber number, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(number);
        var existing = EndpointFor(ChannelType.Sms);
        if (existing is null)
        {
            _endpoints.Add(Endpoint.Create(ChannelType.Sms, number.Value, now));
            return true;
        }

        if (existing.Address == number.Value)
        {
            return false;
        }

        existing.Change(number.Value, now);
        return true;
    }

    /// <summary>Removes the channel's endpoint, and the channel from every priority, so routing stays valid (AC53).</summary>
    public bool RemoveEndpoint(ChannelType channel)
    {
        var existing = EndpointFor(channel);
        if (existing is null)
        {
            return false;
        }

        _endpoints.Remove(existing);
        Routing = Routing.Without(channel);
        return true;
    }

    /// <summary>The channels in <paramref name="channels"/> that this contact has no endpoint for.</summary>
    public IReadOnlyList<ChannelType> ChannelsWithoutEndpoint(IEnumerable<ChannelType> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        return [.. channels.Distinct().Where(c => EndpointFor(c) is null)];
    }

    /// <summary>Sets the ordered channels for one priority. Throws if a channel has no endpoint (AC53). Returns false when unchanged.</summary>
    public bool SetRouting(Priority priority, IReadOnlyList<ChannelType> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var missing = ChannelsWithoutEndpoint(channels);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{Handle} has no endpoint for {string.Join(", ", missing)}, so routing can't use it.");
        }

        var next = Routing.With(priority, channels);
        if (next.Equals(Routing))
        {
            return false;
        }

        Routing = next;
        return true;
    }
}
