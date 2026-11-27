using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationApi.Application.Channels;
using NotificationApi.Application.Common;
using NotificationApi.Application.Contacts;
using NotificationApi.Application.Events;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Notifications;

/// <summary>
/// Makes one attempt for a claimed (<c>sending</c>) delivery through the matching <see cref="INotificationChannel"/>,
/// then records the outcome (spec §5 steps 4–5): <c>sent</c> with the provider's message id, <c>retrying</c> while
/// retries are left, otherwise <c>failed</c>. A final state queues one <c>delivery.sent</c> / <c>delivery.failed</c>
/// event after the commit; the event is best-effort and never changes the outcome (AC34). With no channel
/// implementation for the delivery's channel it fails at once, since retrying can't help.
/// </summary>
public sealed partial class AttemptDelivery(
    INotificationRepository notifications,
    IEnumerable<INotificationChannel> channels,
    IUnitOfWork unitOfWork,
    IPendingHaEvents events,
    DeliveryWakeUp wakeUp,
    IOptions<DeliveryOptions> options,
    TimeProvider time,
    ILogger<AttemptDelivery> logger)
{
    /// <returns>The delivery's status after the attempt, or null if it wasn't waiting for one.</returns>
    public async Task<DeliveryStatus?> HandleAsync(DeliveryId deliveryId, CancellationToken cancellationToken)
    {
        var delivery = await notifications.FindDeliveryAsync(deliveryId, cancellationToken);
        if (delivery is not { Status: DeliveryStatus.Sending })
        {
            LogNotClaimed(logger, deliveryId);
            return null;
        }

        var notification = await notifications.FindAsync(delivery.NotificationId, cancellationToken)
            ?? throw new InvalidOperationException($"Delivery {deliveryId} has no notification {delivery.NotificationId}.");

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["NotificationId"] = notification.Id.ToString(), ["DeliveryId"] = deliveryId.ToString() });
        var channelName = ChannelNames.Of(delivery.Channel);
        var contact = delivery.Contact.ToString();
        var channel = channels.FirstOrDefault(c => c.Type == delivery.Channel);
        if (channel is null)
        {
            delivery.Fail($"The {channelName} channel isn't available in this version of the add-on.", time.GetUtcNow());
        }
        else
        {
            var message = new OutboundMessage(
                delivery.Address, notification.Title, notification.Message, MessageFormatter.Render(delivery.Channel, notification.Title, notification.Message));
            var maskedAddress = delivery.MaskedAddress;
            LogSending(logger, deliveryId, contact, channelName, maskedAddress, delivery.Attempts);
            var result = await SendAsync(channel, message, channelName, cancellationToken);
            var now = time.GetUtcNow();
            if (result.Succeeded)
            {
                delivery.MarkSent(result.ProviderMessageId, now);
            }
            else
            {
                delivery.RecordFailedAttempt(result.Error!, now, options.Value.ToPolicy());
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        Report(notification, delivery, contact, channelName);
        return delivery.Status;
    }

    private async Task<ChannelResult> SendAsync(INotificationChannel channel, OutboundMessage message, string channelName, CancellationToken cancellationToken)
    {
        try
        {
            return await channel.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Shutting down mid-send: the delivery stays "sending" and the restart rule decides (ADR 0014).
        }
#pragma warning disable CA1031 // A channel bug must fail this attempt, not the worker.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogChannelCrashed(logger, channelName, ex);
            return ChannelResult.Failed($"The {channelName} channel failed unexpectedly ({ex.GetType().Name}).");
        }
    }

    private void Report(Notification notification, Delivery delivery, string contact, string channelName)
    {
        switch (delivery.Status)
        {
            case DeliveryStatus.Sent:
                LogSent(logger, delivery.Id, contact, channelName, delivery.Attempts);
                events.Enqueue(HaEvent.DeliveryCompleted(notification, delivery, time));
                break;
            case DeliveryStatus.Failed:
                LogFailed(logger, delivery.Id, contact, channelName, delivery.Attempts, delivery.LastError!);
                events.Enqueue(HaEvent.DeliveryCompleted(notification, delivery, time));
                break;
            case DeliveryStatus.Retrying:
                LogRetrying(logger, delivery.Id, contact, channelName, delivery.Attempts, delivery.LastError!, delivery.NextAttemptAt!.Value);
                wakeUp.Notify(); // so the worker plans its next wake-up for this retry
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Delivery {DeliveryId} isn't waiting for an attempt; skipped.")]
    private static partial void LogNotClaimed(ILogger logger, DeliveryId deliveryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sending delivery {DeliveryId} to {Contact} by {Channel} ({MaskedAddress}), attempt {Attempt}.")]
    private static partial void LogSending(ILogger logger, DeliveryId deliveryId, string contact, string channel, string maskedAddress, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Delivery {DeliveryId} to {Contact} by {Channel} sent (attempt {Attempt}).")]
    private static partial void LogSent(ILogger logger, DeliveryId deliveryId, string contact, string channel, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Delivery {DeliveryId} to {Contact} by {Channel}: attempt {Attempt} failed ({Error}). Retrying at {NextAttemptAt:u}.")]
    private static partial void LogRetrying(ILogger logger, DeliveryId deliveryId, string contact, string channel, int attempt, string error, DateTimeOffset nextAttemptAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delivery {DeliveryId} to {Contact} by {Channel} failed after {Attempts} attempt(s): {Error}")]
    private static partial void LogFailed(ILogger logger, DeliveryId deliveryId, string contact, string channel, int attempts, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The {Channel} channel threw instead of returning a result.")]
    private static partial void LogChannelCrashed(ILogger logger, string channel, Exception exception);
}
