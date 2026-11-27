namespace NotificationApi.Domain.Notifications;

/// <summary>
/// How often a failed delivery is tried again (spec §5 step 4): <see cref="MaxRetries"/> more attempts after the first,
/// <see cref="Interval"/> apart. The default, 3 retries at 30 s, means 4 attempts in total (AC20).
/// </summary>
public sealed record RetryPolicy
{
    public const int MaxAllowedRetries = 20;

    public static readonly RetryPolicy Default = new(3, TimeSpan.FromSeconds(30));

    public RetryPolicy(int maxRetries, TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxRetries, MaxAllowedRetries);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        MaxRetries = maxRetries;
        Interval = interval;
    }

    public int MaxRetries { get; }

    public TimeSpan Interval { get; }

    /// <summary>The most attempts a delivery gets: the first plus the retries.</summary>
    public int MaxAttempts => MaxRetries + 1;
}
