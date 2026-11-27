namespace NotificationApi.Domain.Recipients;

/// <summary>Identifies a <see cref="Contact"/>. Internal only: HA uses the <see cref="Handle"/>.</summary>
public readonly record struct ContactId(Guid Value)
{
    public static ContactId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
