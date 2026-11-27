using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Audit;

/// <summary>The audit log (spec §7, AC82). Append-only from the application's point of view.</summary>
public interface IAuditLogRepository
{
    void Add(AuditEntry entry);

    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Newest first (by insertion order).</summary>
    Task<IReadOnlyList<AuditEntry>> ListNewestFirstAsync(int skip, int take, CancellationToken cancellationToken);
}
