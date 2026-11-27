using Microsoft.Extensions.Logging;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Common;
using NotificationApi.Application.Webhooks;

namespace NotificationApi.Application.Events;

/// <summary>
/// Sends one event to the HA integration's registered webhook (spec §4.4). Best-effort: one attempt, no retry, and a
/// failure is logged but never thrown (AC34). With no registration the event is dropped and logged at Debug.
/// Logs carry the event type, id and HTTP status, never the secret or the body.
/// </summary>
public sealed partial class PublishHaEvent(
    IApiClientRepository clients,
    ISecretProtector protector,
    IHaEventPublisher publisher,
    ILogger<PublishHaEvent> logger)
{
    public async Task<HaEventOutcome> HandleAsync(HaEvent haEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(haEvent);

        var client = await clients.FindByNameAsync(ApiClientNames.HomeAssistant, cancellationToken);
        if (client is not { WebhookUrl: { } url, ProtectedWebhookSecret: { } protectedSecret })
        {
            LogDropped(logger, haEvent.Type, haEvent.Id);
            return HaEventOutcome.NotRegistered(haEvent.Id);
        }

        var secret = protector.Unprotect(protectedSecret);
        if (secret.IsFailure || !WebhookSecret.TryDecode(secret.Value, out var key))
        {
            LogUnreadableSecret(logger, haEvent.Type, haEvent.Id);
            return HaEventOutcome.Failed(haEvent.Id, WebhookErrors.UnreadableSecret.Message, null);
        }

        var result = await publisher.PublishAsync(new WebhookTarget(url, key), haEvent, cancellationToken);
        if (result.Succeeded)
        {
            LogDelivered(logger, haEvent.Type, haEvent.Id, result.StatusCode);
            return HaEventOutcome.Delivered(haEvent.Id, result.StatusCode ?? 0);
        }

        var status = result.StatusCode is { } code ? $"HTTP {code}" : "no response";
        LogFailed(logger, haEvent.Type, haEvent.Id, status, result.Error!);
        return HaEventOutcome.Failed(haEvent.Id, result.Error!, result.StatusCode);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped event {EventType} {EventId}: no webhook is registered.")]
    private static partial void LogDropped(ILogger logger, string eventType, string eventId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Delivered event {EventType} {EventId} (HTTP {StatusCode}).")]
    private static partial void LogDelivered(ILogger logger, string eventType, string eventId, int? statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event {EventType} {EventId} was not delivered ({Status}): {Error}. Events are not retried.")]
    private static partial void LogFailed(ILogger logger, string eventType, string eventId, string status, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event {EventType} {EventId} was not delivered: the webhook secret can't be decrypted. The integration must register its webhook again.")]
    private static partial void LogUnreadableSecret(ILogger logger, string eventType, string eventId);
}
