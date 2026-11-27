using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Notifications;

/// <summary>
/// Hand-written validator for <see cref="SendNotificationCommand"/> (spec §4.1 "Request rules", AC10). Keys are
/// <see cref="NotifyFields"/>. Whether a well-formed handle exists is checked later, by <see cref="SendNotification"/>.
/// </summary>
public static class SendNotificationValidator
{
    /// <returns>Field errors; empty when <paramref name="valid"/> is set.</returns>
    public static IReadOnlyDictionary<string, string[]> Validate(SendNotificationCommand command, out ValidNotificationRequest? valid)
    {
        ArgumentNullException.ThrowIfNull(command);
        valid = null;
        var errors = new Dictionary<string, string[]>();

        var handles = new List<Handle>();
        if (command.To is null || command.To.Count == 0)
        {
            errors[NotifyFields.To] = ["At least one recipient handle is required, e.g. \"contact:alice\" or \"group:family\"."];
        }
        else
        {
            var problems = new List<string>();
            foreach (var value in command.To)
            {
                if (Handle.TryParse(value, out var handle))
                {
                    handles.Add(handle);
                }
                else
                {
                    problems.Add(value is null
                        ? "A handle can't be null."
                        : $"Not a valid handle: '{Shorten(value)}'. Use contact:<name> or group:<name>, with a-z, 0-9 and _.");
                }
            }

            if (problems.Count > 0)
            {
                errors[NotifyFields.To] = [.. problems];
            }
        }

        if (command.Message is null || command.Message.Length == 0)
        {
            errors[NotifyFields.Message] = ["The message is required."];
        }
        else if (command.Message.Length > Notification.MaxMessageLength)
        {
            errors[NotifyFields.Message] = [$"The message must be at most {Notification.MaxMessageLength} characters."];
        }

        CheckLength(errors, NotifyFields.Title, command.Title, Notification.MaxTitleLength);
        CheckLength(errors, NotifyFields.Tag, command.Tag, Notification.MaxTextLength);
        CheckLength(errors, NotifyFields.Source, command.Source, Notification.MaxTextLength);
        CheckLength(errors, NotifyFields.IdempotencyKey, command.IdempotencyKey, Notification.MaxTextLength);

        var priority = Priority.Normal;
        if (command.Priority is null)
        {
            errors[NotifyFields.Priority] = [$"The priority is required: {string.Join(", ", WireNames.PriorityNames)}."];
        }
        else if (!WireNames.TryParsePriority(command.Priority, out priority))
        {
            errors[NotifyFields.Priority] = [$"Unknown priority '{Shorten(command.Priority)}'. Use one of: {string.Join(", ", WireNames.PriorityNames)} (lower case)."];
        }

        var channels = new List<ChannelType>();
        var unknownChannels = new List<string>();
        foreach (var value in command.Channels ?? [])
        {
            if (WireNames.TryParseChannel(value, out var channel))
            {
                channels.Add(channel);
            }
            else
            {
                unknownChannels.Add($"Unknown channel '{Shorten(value ?? "null")}'. Use one of: {string.Join(", ", WireNames.ChannelNames)} (lower case).");
            }
        }

        if (unknownChannels.Count > 0)
        {
            errors[NotifyFields.Channels] = [.. unknownChannels];
        }

        if (command.HasActions)
        {
            errors[NotifyFields.Actions] = ["Actions are reserved for a later version. Omit them, or send null or []."];
        }

        if (errors.Count == 0)
        {
            valid = new ValidNotificationRequest(
                handles, command.Title, command.Message!, priority, [.. channels.Distinct()], command.Tag, command.Source, command.IdempotencyKey);
        }

        return errors;
    }

    private static void CheckLength(Dictionary<string, string[]> errors, string field, string? value, int max)
    {
        if (value is not null && value.Length > max)
        {
            errors[field] = [$"The {field.Replace('_', ' ')} must be at most {max} characters."];
        }
    }

    /// <summary>Echoes at most 70 characters of a bad value.</summary>
    private static string Shorten(string value) => value.Length <= 70 ? value : value[..70] + "…";
}
