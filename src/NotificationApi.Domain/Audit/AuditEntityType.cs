namespace NotificationApi.Domain.Audit;

/// <summary>What an <see cref="AuditEntry"/> is about.</summary>
public enum AuditEntityType
{
    Contact,
    Group,

    /// <summary>A contact's endpoint (e.g. its SMS number). The entity key is the contact's handle.</summary>
    Endpoint,

    /// <summary>A contact's per-priority routing. The entity key is the contact's handle.</summary>
    Routing,

    /// <summary>A group's members. The entity key is the group's handle.</summary>
    Membership,

    ApiToken,

    WebhookEvent,
}
