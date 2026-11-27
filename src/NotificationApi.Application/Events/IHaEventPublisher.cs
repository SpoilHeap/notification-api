namespace NotificationApi.Application.Events;

/// <summary>
/// POSTs one signed event to a webhook (spec §4.4). Implemented in Infrastructure with a typed <c>HttpClient</c>:
/// one attempt, a 5 s timeout and no retry (AC34). Use <see cref="PublishHaEvent"/> rather than calling this directly.
/// </summary>
public interface IHaEventPublisher
{
    Task<WebhookSendResult> PublishAsync(WebhookTarget target, HaEvent haEvent, CancellationToken cancellationToken);
}

/// <summary>Where to send, and the HMAC key: the decoded bytes of the registered secret.</summary>
public sealed class WebhookTarget
{
    public WebhookTarget(Uri url, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(secret);
        Url = url;
        Secret = secret;
    }

    public Uri Url { get; }

#pragma warning disable CA1819 // The HMAC key is bytes; it isn't exposed beyond the publisher.
    public byte[] Secret { get; }
#pragma warning restore CA1819

    public override string ToString() => $"WebhookTarget({Url.Host}:{Url.Port}, secret redacted)";
}

/// <summary>Outcome of a single POST. <see cref="StatusCode"/> is set whenever the webhook answered.</summary>
public sealed record WebhookSendResult
{
    private WebhookSendResult(int? statusCode, string? error)
    {
        StatusCode = statusCode;
        Error = error;
    }

    public bool Succeeded => Error is null;

    public int? StatusCode { get; }

    /// <summary>Why it failed. Never contains the secret or the body.</summary>
    public string? Error { get; }

    public static WebhookSendResult Delivered(int statusCode) => new(statusCode, null);

    public static WebhookSendResult Failed(string error, int? statusCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new(statusCode, error);
    }
}
