using System.Net;

namespace NotificationApi.Domain.Notifications;

/// <summary>
/// Renders a title and message for one channel (spec §5 "Formatting"). Telegram gets HTML: <c>&lt;b&gt;title&lt;/b&gt;</c>,
/// a newline, then the message, with both HTML-escaped so user text can't inject markup (AC24). SMS gets plain text:
/// <c>title: message</c> (AC25). With no title, just the (escaped, for Telegram) message.
/// </summary>
public static class MessageFormatter
{
    public static string Render(ChannelType channel, string? title, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return channel switch
        {
            ChannelType.Telegram => Telegram(title, message),
            ChannelType.Sms => Sms(title, message),
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown channel."),
        };
    }

    /// <summary>Telegram <c>parse_mode=HTML</c>. Only <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c> (and quotes) need escaping there.</summary>
    public static string Telegram(string? title, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var body = WebUtility.HtmlEncode(message);
        return string.IsNullOrEmpty(title) ? body : $"<b>{WebUtility.HtmlEncode(title)}</b>\n{body}";
    }

    public static string Sms(string? title, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return string.IsNullOrEmpty(title) ? message : $"{title}: {message}";
    }
}
