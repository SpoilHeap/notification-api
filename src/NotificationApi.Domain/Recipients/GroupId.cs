namespace NotificationApi.Domain.Recipients;

/// <summary>Identifies a <see cref="Group"/>. Internal only: HA uses the <see cref="Handle"/>.</summary>
public readonly record struct GroupId(Guid Value)
{
    public static GroupId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
