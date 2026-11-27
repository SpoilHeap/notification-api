namespace NotificationApi.Domain.Audit;

public enum AuditAction
{
    Create,
    Update,
    Delete,

    /// <summary>A secret was shown in the UI (the API token).</summary>
    Reveal,

    /// <summary>Something was sent on demand (the webhook test event).</summary>
    Send,
}
