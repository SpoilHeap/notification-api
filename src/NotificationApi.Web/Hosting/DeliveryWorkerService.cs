using System.Collections.Concurrent;
using NotificationApi.Application.Notifications;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Web.Hosting;

/// <summary>
/// The delivery worker (spec §5, ADR 0014): a thin loop around the delivery use cases. On start it runs
/// <see cref="RecoverInterruptedDeliveries"/>. Then it repeatedly claims due deliveries with
/// <see cref="ProcessDueDeliveries"/> and runs <see cref="AttemptDelivery"/> for each one in its own scope and task,
/// without waiting for them, so a slow or failing delivery never holds up another (AC26). It sleeps until the next
/// retry is due or <see cref="DeliveryWakeUp"/> signals new work, with a polling safety net.
/// On shutdown it stops claiming and lets attempts in flight finish, so a graceful stop leaves no delivery
/// <c>sending</c>; only if the host stops waiting are they cancelled.
/// </summary>
internal sealed partial class DeliveryWorkerService(
    IServiceScopeFactory scopes,
    DeliveryWakeUp wakeUp,
    TimeProvider time,
    ILogger<DeliveryWorkerService> logger) : BackgroundService
{
    /// <summary>The longest sleep with nothing scheduled: a safety net, since new work also wakes the worker.</summary>
    public static readonly TimeSpan IdlePoll = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<DeliveryId, Task> _inFlight = new();
    private readonly CancellationTokenSource _abort = new();

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            await Task.WhenAll(_inFlight.Values).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await _abort.CancelAsync(); // the host gave up waiting: those deliveries stay "sending" (ADR 0014)
        }
    }

    public override void Dispose()
    {
        _abort.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = await ClaimAndStartAsync(stoppingToken);
                await wakeUp.WaitAsync(wait, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: queued and retrying deliveries stay in the database for the next start (AC23).
        }
    }

    private async Task RecoverAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RecoverInterruptedDeliveries>().HandleAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // The worker must keep running; the next start tries again.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRecoveryFailed(logger, ex);
        }
    }

    /// <returns>How long to sleep before looking again (unless woken).</returns>
    private async Task<TimeSpan> ClaimAndStartAsync(CancellationToken stoppingToken)
    {
        DueDeliveries due;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            due = await scope.ServiceProvider.GetRequiredService<ProcessDueDeliveries>().HandleAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // E.g. the database is briefly locked: try again shortly.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogClaimFailed(logger, ex);
            return ErrorBackoff;
        }

        foreach (var id in due.Claimed)
        {
            Start(id);
        }

        if (due.Claimed.Count == ProcessDueDeliveries.BatchSize)
        {
            return TimeSpan.Zero; // more may be due already
        }

        if (due.NextDueAt is not { } next)
        {
            return IdlePoll;
        }

        var untilDue = next - time.GetUtcNow();
        return untilDue <= TimeSpan.Zero ? TimeSpan.Zero : untilDue < IdlePoll ? untilDue : IdlePoll;
    }

    private void Start(DeliveryId id)
    {
        var attempt = Task.Run(() => AttemptAsync(id), CancellationToken.None);
        _inFlight[id] = attempt;
        _ = attempt.ContinueWith(_ => _inFlight.TryRemove(KeyValuePair.Create(id, attempt)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task AttemptAsync(DeliveryId id)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AttemptDelivery>().HandleAsync(id, _abort.Token);
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested)
        {
            // Cut off at shutdown; recovered on the next start.
        }
#pragma warning disable CA1031 // One delivery's failure must not affect the others (AC26).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogAttemptFailed(logger, id, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Recovering interrupted deliveries failed.")]
    private static partial void LogRecoveryFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Looking for due deliveries failed; trying again shortly.")]
    private static partial void LogClaimFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The attempt for delivery {DeliveryId} failed unexpectedly. It stays 'sending' until the next start, which marks it failed.")]
    private static partial void LogAttemptFailed(ILogger logger, DeliveryId deliveryId, Exception exception);
}
