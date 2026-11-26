namespace NotificationApi.Application.Channels;

/// <summary>One message to one endpoint, already resolved from handles and routing.</summary>
/// <param name="Address">Channel-specific address: a Telegram chat id or an E.164 phone number.</param>
/// <param name="Title">Optional title, rendered per channel (spec AC24/AC25).</param>
/// <param name="Body">The message text, as sent by HA.</param>
public sealed record OutboundMessage(string Address, string? Title, string Body);
