namespace NotificationApi.Application.Webhooks;

/// <summary><c>PUT /webhook</c> input, before validation (spec §4.3).</summary>
/// <param name="Url">Absolute http(s) URL that events are POSTed to.</param>
/// <param name="Secret">Standard base64 of at least 32 random bytes.</param>
public sealed record RegisterWebhookCommand(string? Url, string? Secret)
{
    /// <summary>Redacted, so the secret can't reach a log through this record.</summary>
    public override string ToString() => $"RegisterWebhookCommand {{ Url = {Url}, Secret = (redacted) }}";
}

/// <summary>A registration as the API shows it: never the secret.</summary>
public sealed record WebhookRegistration(Uri Url, DateTimeOffset RegisteredAt);
