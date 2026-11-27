using NotificationApi.Domain.Notifications;

namespace NotificationApi.Domain.Recipients;

/// <summary>
/// Where a contact receives one channel: an E.164 number for SMS, a chat id for Telegram. A contact has at most one
/// endpoint per channel. Only verified endpoints receive deliveries (spec §5 step 2).
/// </summary>
public sealed class Endpoint
{
    private Endpoint(ChannelType channel, string address, DateTimeOffset? verifiedAt)
    {
        Channel = channel;
        Address = address;
        VerifiedAt = verifiedAt;
    }

    public ChannelType Channel { get; private set; }

    /// <summary>The full address. Never log it: use <see cref="MaskedAddress"/>.</summary>
    public string Address { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public bool IsVerified => VerifiedAt is not null;

    /// <summary>What the UI and the audit log show.</summary>
    public string MaskedAddress => Channel == ChannelType.Sms && PhoneNumber.TryParse(Address, out var number) ? number.Masked : "linked";

    internal static Endpoint Create(ChannelType channel, string address, DateTimeOffset? verifiedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return new Endpoint(channel, address, verifiedAt);
    }

    internal void Change(string address, DateTimeOffset? verifiedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Address = address;
        VerifiedAt = verifiedAt;
    }

    public override string ToString() => $"{Channel} {MaskedAddress}";
}
