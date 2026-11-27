using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Contacts;

/// <summary>Channel and priority names as people read them.</summary>
public static class ChannelNames
{
    public static string Of(ChannelType channel) => channel switch
    {
        ChannelType.Sms => "SMS",
        ChannelType.Telegram => "Telegram",
        _ => channel.ToString(),
    };

    public static string Of(Priority priority) => priority.ToString().ToLowerInvariant();
}
