using NotificationApi.Application.Common;

namespace NotificationApi.Application.Webhooks;

public static class WebhookErrors
{
    public static readonly Error UnknownClient = new("webhook.unknown_client", "The API client no longer exists.");

    public static readonly Error UnreadableSecret = new(
        "webhook.secret_unreadable",
        "The webhook secret can't be decrypted (the Data Protection keys in /data/keys may be missing or replaced). Reload the integration in Home Assistant to register the webhook again.");
}
