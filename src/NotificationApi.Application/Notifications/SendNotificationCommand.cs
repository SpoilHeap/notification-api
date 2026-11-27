using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Notifications;

/// <summary><c>POST /notify</c> as received, before validation (spec §4.1). Null means omitted.</summary>
/// <param name="To">Recipient handles.</param>
/// <param name="Title">Optional title.</param>
/// <param name="Message">The body.</param>
/// <param name="Priority"><c>low | normal | high | critical</c>.</param>
/// <param name="Channels">Optional override; empty means none.</param>
/// <param name="Tag">Optional, logged and passed back in events.</param>
/// <param name="Source">Optional, e.g. the automation's entity id.</param>
/// <param name="IdempotencyKey">Optional; the same key within 24 h replays the first answer (AC12).</param>
/// <param name="HasActions">True when <c>actions</c> was given as anything other than null or <c>[]</c> (reserved for v2).</param>
public sealed record SendNotificationCommand(
    IReadOnlyList<string?>? To,
    string? Title,
    string? Message,
    string? Priority,
    IReadOnlyList<string?>? Channels,
    string? Tag,
    string? Source,
    string? IdempotencyKey,
    bool HasActions);

/// <summary>A command that passed <see cref="SendNotificationValidator"/>.</summary>
public sealed record ValidNotificationRequest(
    IReadOnlyList<Handle> To,
    string? Title,
    string Message,
    Priority Priority,
    IReadOnlyList<ChannelType> Channels,
    string? Tag,
    string? Source,
    string? IdempotencyKey);

/// <summary>The JSON field names of <c>POST /notify</c>, used as keys of field errors.</summary>
public static class NotifyFields
{
    public const string To = "to";
    public const string Title = "title";
    public const string Message = "message";
    public const string Priority = "priority";
    public const string Channels = "channels";
    public const string Tag = "tag";
    public const string Actions = "actions";
    public const string Source = "source";
    public const string IdempotencyKey = "idempotency_key";
}
