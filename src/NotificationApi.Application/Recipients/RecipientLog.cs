using Microsoft.Extensions.Logging;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Recipients;

/// <summary>Log messages for contact and group changes. Handles and masked numbers only, never a full number (AC74).</summary>
internal static partial class RecipientLog
{
    /// <param name="logger">The use case's logger.</param>
    /// <param name="haUserId">Who made the change.</param>
    /// <param name="change">A past-tense verb: created, renamed, deleted, …</param>
    /// <param name="handle">The contact or group.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "HA user '{HaUserId}' {Change} {Handle}.")]
    public static partial void Changed(ILogger logger, string haUserId, string change, Handle handle);

    [LoggerMessage(Level = LogLevel.Information, Message = "HA user '{HaUserId}' set the SMS number of {Handle} to {MaskedNumber}.")]
    public static partial void SmsNumberSet(ILogger logger, string haUserId, Handle handle, string maskedNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "HA user '{HaUserId}' removed the {Channel} endpoint of {Handle}.")]
    public static partial void EndpointRemoved(ILogger logger, string haUserId, ChannelType channel, Handle handle);

    [LoggerMessage(Level = LogLevel.Information, Message = "HA user '{HaUserId}' changed the members of {Group}: {Change} {Member}.")]
    public static partial void MembershipChanged(ILogger logger, string haUserId, Handle group, string change, Handle member);

    public static void Rejected(ILogger logger, string target, IEnumerable<string> fields)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            var list = string.Join(", ", fields);
            LogRejected(logger, target, list);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected a change to {Target}: invalid {Fields}.")]
    private static partial void LogRejected(ILogger logger, string target, string fields);
}
