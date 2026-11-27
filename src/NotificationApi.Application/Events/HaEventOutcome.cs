namespace NotificationApi.Application.Events;

public enum HaEventOutcomeKind
{
    /// <summary>The webhook answered with a 2xx status.</summary>
    Delivered,

    /// <summary>The webhook answered with another status, timed out or was unreachable, or the secret couldn't be read.</summary>
    Failed,

    /// <summary>No webhook is registered, so the event was dropped.</summary>
    NotRegistered,
}

/// <summary>What happened to one event. Events are best-effort, so none of these is an error for the caller (AC34).</summary>
public sealed record HaEventOutcome(HaEventOutcomeKind Kind, string EventId, int? StatusCode, string? Error)
{
    public static HaEventOutcome Delivered(string eventId, int statusCode) => new(HaEventOutcomeKind.Delivered, eventId, statusCode, null);

    public static HaEventOutcome Failed(string eventId, string error, int? statusCode) => new(HaEventOutcomeKind.Failed, eventId, statusCode, error);

    public static HaEventOutcome NotRegistered(string eventId) => new(HaEventOutcomeKind.NotRegistered, eventId, null, null);
}
