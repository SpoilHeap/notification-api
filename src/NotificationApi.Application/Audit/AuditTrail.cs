using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Audit;

/// <summary>
/// Adds an audit entry to the current unit of work, stamped with the current time. The calling use case commits it
/// together with the change it describes, so an entry exists exactly when the change was saved (AC82).
/// </summary>
public sealed class AuditTrail(IAuditLogRepository log, TimeProvider time)
{
    public void Record(AuditActor actor, AuditAction action, AuditEntityType entityType, string entityKey, string summary) =>
        log.Add(AuditEntry.Record(actor, action, entityType, entityKey, summary, time.GetUtcNow()));
}
