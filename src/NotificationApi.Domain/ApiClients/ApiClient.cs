namespace NotificationApi.Domain.ApiClients;

/// <summary>
/// A caller of <c>/api/v1</c>. In v1 there is exactly one: the HA integration (spec §7).
/// The token is stored twice: as a SHA-256 hash for verification, and encrypted so it can be
/// re-published through Supervisor discovery on every start (ADR 0012).
/// </summary>
public sealed class ApiClient
{
    public const int MaxNameLength = 64;

    /// <summary>How often <see cref="LastUsedAt"/> is refreshed, so each API call doesn't write to the database.</summary>
    public static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    private ApiClient(ApiClientId id, string name, ApiTokenHash tokenHash, string protectedToken, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        TokenHash = tokenHash;
        ProtectedToken = protectedToken;
        CreatedAt = createdAt;
    }

    public ApiClientId Id { get; private set; }

    public string Name { get; private set; }

    public ApiTokenHash TokenHash { get; private set; }

    /// <summary>The plaintext token encrypted with ASP.NET Data Protection. Opaque to the domain.</summary>
    public string ProtectedToken { get; private set; }

    /// <summary>The HA webhook registered by the integration (spec §4.3). Not set until the integration registers it.</summary>
    public Uri? WebhookUrl { get; private set; }

    /// <summary>The webhook's shared secret, encrypted with Data Protection.</summary>
    public string? ProtectedWebhookSecret { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <param name="name">A stable name, e.g. <c>home_assistant</c>.</param>
    /// <param name="token">The new token. Only its hash is kept in clear.</param>
    /// <param name="protectedToken">The same token, already encrypted by the caller.</param>
    /// <param name="now">Creation time.</param>
    public static ApiClient Create(string name, ApiToken token, string protectedToken, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedToken);
        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException($"API client names are at most {MaxNameLength} characters.", nameof(name));
        }

        return new ApiClient(ApiClientId.New(), name, token.ComputeHash(), protectedToken, now);
    }

    public bool Accepts(ApiToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return TokenHash.Matches(token);
    }

    /// <summary>Records a successful call. Returns false when nothing changed (within <see cref="LastUsedResolution"/>).</summary>
    public bool RecordUse(DateTimeOffset now)
    {
        if (LastUsedAt is { } last && now - last < LastUsedResolution)
        {
            return false;
        }

        LastUsedAt = now;
        return true;
    }
}
