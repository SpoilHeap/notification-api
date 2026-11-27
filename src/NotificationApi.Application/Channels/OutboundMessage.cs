namespace NotificationApi.Application.Channels;

/// <summary>One message to one endpoint, already resolved from handles and routing.</summary>
/// <param name="Address">Channel-specific address: a Telegram chat id or an E.164 phone number. Never log it in full.</param>
/// <param name="Title">Optional title, as sent by HA.</param>
/// <param name="Body">The message text, as sent by HA. Never log it at Information or above.</param>
/// <param name="Text">
/// What the channel sends: <paramref name="Title"/> and <paramref name="Body"/> rendered for this channel by
/// <see cref="Domain.Notifications.MessageFormatter"/> (AC24, AC25).
/// </param>
public sealed record OutboundMessage(string Address, string? Title, string Body, string Text)
{
    public override string ToString() => $"OutboundMessage {{ {Text.Length} characters, address redacted }}";
}
