using NotificationApi.Application.Events;

namespace NotificationApi.Web.Hosting;

/// <summary>
/// Sends queued HA events (e.g. <c>recipients.changed</c>) in the background, one at a time and in order, so a UI
/// request never waits for the webhook. A thin loop around <see cref="PublishHaEvent"/>, which logs every outcome.
/// </summary>
internal sealed partial class HaEventSenderService(
    IPendingHaEvents pending,
    IServiceScopeFactory scopes,
    ILogger<HaEventSenderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var haEvent in pending.ReadAllAsync(stoppingToken))
            {
                await SendAsync(haEvent, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Waiting events are dropped; the integration also polls GET /recipients.
        }
    }

    private async Task SendAsync(HaEvent haEvent, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PublishHaEvent>().HandleAsync(haEvent, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // One bad event (e.g. the database is briefly locked) must not stop later ones.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogUnexpectedFailure(logger, haEvent.Type, haEvent.Id, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event {EventType} {EventId} was not sent: unexpected failure. Events are not retried.")]
    private static partial void LogUnexpectedFailure(ILogger logger, string eventType, string eventId, Exception exception);
}
