using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Events;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Webhooks;

/// <summary>
/// The Integration page's <strong>Send test event</strong> action: a <c>webhook.test</c> event with <c>data: {}</c>.
/// Sent inline (not queued), because the page shows HA's answer. Written to the audit log with the outcome (AC82).
/// </summary>
public sealed partial class SendTestEvent(
    PublishHaEvent publishHaEvent,
    AuditTrail audit,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<SendTestEvent> logger)
{
    /// <param name="actor">The HA user asking, from the Ingress identity headers.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    public async Task<HaEventOutcome> HandleAsync(AuditActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var haEvent = HaEvent.WebhookTest(time);
        LogRequested(logger, haEvent.Id, actor.HaUserId);
        var outcome = await publishHaEvent.HandleAsync(haEvent, cancellationToken);

        var result = outcome.Kind switch
        {
            HaEventOutcomeKind.Delivered => $"delivered (HTTP {outcome.StatusCode})",
            HaEventOutcomeKind.NotRegistered => "not sent: no webhook is registered",
            _ => outcome.StatusCode is { } status ? $"failed (HTTP {status})" : "failed (no response)",
        };
        audit.Record(actor, AuditAction.Send, AuditEntityType.WebhookEvent, haEvent.Id, $"Sent a {haEvent.Type} event: {result}");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return outcome;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "HA user '{HaUserId}' sent test event {EventId} from the web UI.")]
    private static partial void LogRequested(ILogger logger, string eventId, string haUserId);
}
