namespace NotificationApi.Domain.Notifications;

/// <summary>Where a <see cref="Delivery"/> is in its life: <c>Queued → Sending → Sent | Retrying → … → Failed</c> (spec §5).</summary>
public enum DeliveryStatus
{
    /// <summary>Created, not attempted yet. Due at once.</summary>
    Queued,

    /// <summary>An attempt has started and its outcome isn't recorded yet.</summary>
    Sending,

    /// <summary>The provider accepted the message. Final ("sent" is final, spec §2).</summary>
    Sent,

    /// <summary>An attempt failed; another is due at <see cref="Delivery.NextAttemptAt"/>.</summary>
    Retrying,

    /// <summary>Every attempt failed, or the delivery can't be attempted. Final.</summary>
    Failed,
}
