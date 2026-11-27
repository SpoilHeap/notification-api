using Microsoft.Extensions.Options;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Notifications;

/// <summary>
/// Retry settings for deliveries (config section <c>Delivery</c>, spec §5 step 4). App options with no UI in v1:
/// 3 retries at 30 s means 4 attempts in total (AC20).
/// </summary>
public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    public int MaxRetries { get; set; } = RetryPolicy.Default.MaxRetries;

    public TimeSpan RetryInterval { get; set; } = RetryPolicy.Default.Interval;

    public RetryPolicy ToPolicy() => new(MaxRetries, RetryInterval);
}

internal sealed class DeliveryOptionsValidator : IValidateOptions<DeliveryOptions>
{
    public ValidateOptionsResult Validate(string? name, DeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.MaxRetries is < 0 or > RetryPolicy.MaxAllowedRetries)
        {
            failures.Add($"Delivery:MaxRetries must be 0-{RetryPolicy.MaxAllowedRetries}.");
        }

        if (options.RetryInterval <= TimeSpan.Zero)
        {
            failures.Add("Delivery:RetryInterval must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
