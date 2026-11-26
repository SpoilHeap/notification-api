namespace NotificationApi.Domain.Notifications;

/// <summary>Delivery channels supported in v1 (spec §6).</summary>
public enum ChannelType
{
    Telegram,
    Sms,
}
