using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Audit;

/// <summary>
/// One change made in the web UI, and who made it (spec §7, §8.2, AC82). Write-only: entries are never edited, only
/// purged by retention (AC73). The summary never holds a full phone number: callers pass masked values, and any
/// E.164 number that slips through is masked here too (engineering_standards.md §5).
/// </summary>
public sealed class AuditEntry
{
    public const int MaxEntityKeyLength = 200;
    public const int MaxSummaryLength = 500;

    private AuditEntry(
        DateTimeOffset occurredAt,
        string haUserId,
        string haUserDisplayName,
        AuditAction action,
        AuditEntityType entityType,
        string entityKey,
        string summary)
    {
        OccurredAt = occurredAt;
        HaUserId = haUserId;
        HaUserDisplayName = haUserDisplayName;
        Action = action;
        EntityType = entityType;
        EntityKey = entityKey;
        Summary = summary;
    }

    /// <summary>Assigned by the database, in insertion order.</summary>
    public long Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string HaUserId { get; private set; }

    public string HaUserDisplayName { get; private set; }

    public AuditAction Action { get; private set; }

    public AuditEntityType EntityType { get; private set; }

    /// <summary>The handle (e.g. <c>contact:alice</c>) or another stable key of the entity.</summary>
    public string EntityKey { get; private set; }

    /// <summary>A short, human-readable description, with phone numbers masked.</summary>
    public string Summary { get; private set; }

    public static AuditEntry Record(
        AuditActor actor,
        AuditAction action,
        AuditEntityType entityType,
        string entityKey,
        string summary,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        return new AuditEntry(
            now,
            actor.HaUserId,
            actor.DisplayName,
            action,
            entityType,
            Clean(entityKey, MaxEntityKeyLength),
            Clean(summary, MaxSummaryLength));
    }

    private static string Clean(string value, int maxLength)
    {
        var masked = PhoneNumber.MaskAll(value.Trim());
        return masked.Length <= maxLength ? masked : masked[..(maxLength - 1)] + "…";
    }
}
