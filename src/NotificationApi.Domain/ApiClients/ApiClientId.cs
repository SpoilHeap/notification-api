namespace NotificationApi.Domain.ApiClients;

/// <summary>Identifies an <see cref="ApiClient"/>.</summary>
public readonly record struct ApiClientId(Guid Value)
{
    public static ApiClientId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
