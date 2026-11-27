namespace NotificationApi.Application.Discovery;

public enum DiscoveryState
{
    Pending,
    Skipped,
    Published,
    Failed,
}

/// <summary>The latest discovery outcome, shown on the Integration page (spec §9.4 fallback).</summary>
public sealed record DiscoveryStatus(DiscoveryState State, string? Uuid, string? Error, int Attempts, DateTimeOffset? At)
{
    public static readonly DiscoveryStatus Pending = new(DiscoveryState.Pending, null, null, 0, null);

    public static DiscoveryStatus Skipped(string reason, DateTimeOffset at) => new(DiscoveryState.Skipped, null, reason, 0, at);

    public static DiscoveryStatus Published(string uuid, int attempts, DateTimeOffset at) => new(DiscoveryState.Published, uuid, null, attempts, at);

    /// <summary>Still pending: an attempt failed and another is scheduled.</summary>
    public static DiscoveryStatus Retrying(string error, int attempts, DateTimeOffset at) => new(DiscoveryState.Pending, null, error, attempts, at);

    public static DiscoveryStatus Failed(string error, int attempts, DateTimeOffset at) => new(DiscoveryState.Failed, null, error, attempts, at);
}

/// <summary>Holds the latest <see cref="DiscoveryStatus"/> for this process. Registered as a singleton.</summary>
public sealed class DiscoveryStatusStore
{
    private volatile DiscoveryStatus _current = DiscoveryStatus.Pending;

    public DiscoveryStatus Current => _current;

    public void Set(DiscoveryStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _current = status;
    }
}
