namespace NotificationApi.Application.Channels;

/// <summary>Outcome of a single send attempt.</summary>
public sealed record ChannelResult
{
    private ChannelResult(bool succeeded, string? providerMessageId, string? error)
    {
        Succeeded = succeeded;
        ProviderMessageId = providerMessageId;
        Error = error;
    }

    public bool Succeeded { get; }

    /// <summary>The provider's id for the sent message. Stored for SMS.to and for v2 two-way messaging.</summary>
    public string? ProviderMessageId { get; }

    public string? Error { get; }

    public static ChannelResult Sent(string? providerMessageId) => new(true, providerMessageId, null);

    public static ChannelResult Failed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new(false, null, error);
    }
}
