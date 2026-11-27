using Microsoft.Extensions.Logging;
using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Notifications;

/// <summary>
/// <c>POST /notify</c> (spec §4.1, §5 steps 1–3). Validates, expands the handles, picks the channels, then saves the
/// notification and all its deliveries in one commit and wakes the delivery worker. It never sends anything itself,
/// so it answers quickly whatever the providers do (AC13). All-or-nothing: an unknown handle (AC09) or zero
/// deliveries (AC08) saves nothing.
/// </summary>
public sealed partial class SendNotification(
    INotificationRepository notifications,
    IContactRepository contacts,
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    DeliveryWakeUp wakeUp,
    TimeProvider time,
    ILogger<SendNotification> logger)
{
    /// <summary>How long an idempotency key is remembered (AC12).</summary>
    public static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);

    public async Task<Result<NotificationReceipt>> HandleAsync(SendNotificationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();

        // The key alone decides (spec §4.1): a repeat within 24 h gets the first answer, whatever its payload.
        if (command.IdempotencyKey is { Length: > 0 and <= Notification.MaxTextLength } key
            && await notifications.FindByIdempotencyKeyAsync(key, now - IdempotencyWindow, cancellationToken) is { } original)
        {
            var receipt = NotificationReceipt.From(original, await notifications.ListDeliveriesAsync(original.Id, cancellationToken));
            LogReplayed(logger, original.Id);
            return receipt;
        }

        var errors = SendNotificationValidator.Validate(command, out var request);
        if (request is null)
        {
            var fields = string.Join(", ", errors.Keys);
            LogRejected(logger, fields);
            return Error.Validation(errors);
        }

        var expansion = RecipientExpansion.Expand(
            request.To, await contacts.ListAsync(cancellationToken), await groups.ListAsync(cancellationToken));
        if (expansion.UnknownHandles.Count > 0)
        {
            var unknown = expansion.UnknownHandles.Select(h => h.ToString()).Distinct().ToList();
            var list = string.Join(", ", unknown);
            LogUnknownHandles(logger, list);
            return NotificationErrors.UnknownHandles(unknown);
        }

        var plan = DeliveryPlanner.Plan(expansion.Recipients, request.Priority, request.Channels);
        if (plan.Count == 0)
        {
            var resolved = expansion.Recipients.Count;
            LogNoDeliveries(logger, resolved);
            return NotificationErrors.NoDeliveries(resolved);
        }

        var notification = Notification.Create(
            request.Title, request.Message, request.Priority, request.Channels, request.Tag, request.Source, request.IdempotencyKey, now);
        var deliveries = notification.CreateDeliveries(plan, now);
        notifications.Add(notification, deliveries);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        wakeUp.Notify();

        var priority = WireNames.Of(request.Priority);
        LogAccepted(logger, notification.Id, deliveries.Count, priority, request.Tag, request.Source);
        return NotificationReceipt.From(notification, deliveries);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Accepted notification {NotificationId}: {DeliveryCount} delivery(ies), priority {Priority}, tag {Tag}, source {Source}.")]
    private static partial void LogAccepted(ILogger logger, NotificationId notificationId, int deliveryCount, string priority, string? tag, string? source);

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered a repeated idempotency key with notification {NotificationId}; nothing new was queued.")]
    private static partial void LogReplayed(ILogger logger, NotificationId notificationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected a notification: invalid {Fields}.")]
    private static partial void LogRejected(ILogger logger, string fields);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected a notification: unknown handle(s) {Handles}.")]
    private static partial void LogUnknownHandles(ILogger logger, string handles);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected a notification: its {ContactCount} resolved contact(s) gave no deliveries.")]
    private static partial void LogNoDeliveries(ILogger logger, int contactCount);
}
