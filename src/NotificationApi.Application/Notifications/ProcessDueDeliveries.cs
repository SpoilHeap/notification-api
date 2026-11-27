using Microsoft.Extensions.Logging;
using NotificationApi.Application.Common;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Notifications;

/// <param name="Claimed">Deliveries moved to <c>sending</c>, each to be attempted with <see cref="AttemptDelivery"/>.</param>
/// <param name="NextDueAt">When the next queued or retrying delivery is due, or null if none is waiting.</param>
public sealed record DueDeliveries(IReadOnlyList<DeliveryId> Claimed, DateTimeOffset? NextDueAt);

/// <summary>
/// Claims the deliveries that are due (<c>queued</c>, or <c>retrying</c> with <c>next_attempt_at</c> ≤ now): each
/// starts an attempt (<c>→ sending</c>, attempts + 1) and the claim is committed <em>before</em> anything is sent,
/// so a crash mid-send leaves a visible <c>sending</c> row (ADR 0014). The caller (the delivery worker) then runs
/// <see cref="AttemptDelivery"/> for each claimed delivery independently and concurrently (AC26).
/// </summary>
public sealed partial class ProcessDueDeliveries(
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<ProcessDueDeliveries> logger)
{
    /// <summary>The most deliveries claimed at once. Anything left over is due straight away and claimed next time.</summary>
    public const int BatchSize = 50;

    public async Task<DueDeliveries> HandleAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await notifications.ListDueDeliveriesAsync(now, BatchSize, cancellationToken);
        foreach (var delivery in due)
        {
            delivery.StartAttempt(now);
        }

        if (due.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            LogClaimed(logger, due.Count);
        }

        return new DueDeliveries([.. due.Select(d => d.Id)], await notifications.NextAttemptAtAsync(cancellationToken));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Claimed {Count} due delivery(ies).")]
    private static partial void LogClaimed(ILogger logger, int count);
}
