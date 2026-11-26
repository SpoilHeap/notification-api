namespace NotificationApi.Domain.Notifications;

/// <summary>How urgent a notification is. Each contact maps priorities to channels (spec §5).</summary>
public enum Priority
{
    Low,
    Normal,
    High,
    Critical,
}
