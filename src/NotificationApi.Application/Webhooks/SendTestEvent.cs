using Microsoft.Extensions.Logging;
using NotificationApi.Application.Events;

namespace NotificationApi.Application.Webhooks;

/// <summary>The Integration page's <strong>Send test event</strong> action: a <c>webhook.test</c> event with <c>data: {}</c>.</summary>
public sealed partial class SendTestEvent(PublishHaEvent publishHaEvent, TimeProvider time, ILogger<SendTestEvent> logger)
{
    /// <param name="haUserId">The HA user asking, from the Ingress identity headers.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    public async Task<HaEventOutcome> HandleAsync(string haUserId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(haUserId);

        var haEvent = HaEvent.WebhookTest(time);
        // TODO(AuditLog, spec AC82): record this in the audit log once it exists.
        LogRequested(logger, haEvent.Id, haUserId);
        return await publishHaEvent.HandleAsync(haEvent, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "HA user '{HaUserId}' sent test event {EventId} from the web UI.")]
    private static partial void LogRequested(ILogger logger, string eventId, string haUserId);
}
