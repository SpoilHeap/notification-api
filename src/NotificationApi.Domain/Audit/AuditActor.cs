namespace NotificationApi.Domain.Audit;

/// <summary>Who made a change: the HA user from the Ingress identity headers (spec §8.2).</summary>
public sealed record AuditActor
{
    public const int MaxLength = 200;

    public AuditActor(string haUserId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(haUserId);
        HaUserId = Truncate(haUserId.Trim());
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? HaUserId : Truncate(displayName.Trim());
    }

    public string HaUserId { get; }

    public string DisplayName { get; }

    private static string Truncate(string value) => value.Length <= MaxLength ? value : value[..MaxLength];
}
