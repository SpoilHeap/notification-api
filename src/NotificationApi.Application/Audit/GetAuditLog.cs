using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Audit;

/// <summary>One row of the Audit log page.</summary>
public sealed record AuditEntryView(
    DateTimeOffset OccurredAt,
    string HaUserId,
    string HaUserDisplayName,
    AuditAction Action,
    AuditEntityType EntityType,
    string EntityKey,
    string Summary);

/// <param name="Entries">This page's entries, newest first.</param>
/// <param name="Page">1-based, clamped to the available pages.</param>
/// <param name="PageCount">At least 1.</param>
/// <param name="Total">Entries in the log.</param>
public sealed record AuditLogPage(IReadOnlyList<AuditEntryView> Entries, int Page, int PageCount, int Total);

/// <summary>The read-only Audit log page: newest first, <see cref="PageSize"/> per page.</summary>
public sealed class GetAuditLog(IAuditLogRepository log)
{
    public const int PageSize = 50;

    // TODO(retention, spec AC73): the daily purge deletes entries older than the retention period. Not built yet.
    public async Task<AuditLogPage> HandleAsync(int page, CancellationToken cancellationToken)
    {
        var total = await log.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, pageCount);

        var entries = await log.ListNewestFirstAsync((page - 1) * PageSize, PageSize, cancellationToken);
        return new AuditLogPage(
            [.. entries.Select(e => new AuditEntryView(e.OccurredAt, e.HaUserId, e.HaUserDisplayName, e.Action, e.EntityType, e.EntityKey, e.Summary))],
            page,
            pageCount,
            total);
    }
}
