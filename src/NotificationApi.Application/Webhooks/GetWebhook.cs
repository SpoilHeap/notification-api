using NotificationApi.Application.ApiAccess;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.Webhooks;

/// <summary><c>GET /webhook</c> and the Integration page: the registration without the secret, or null.</summary>
public sealed class GetWebhook(IApiClientRepository clients)
{
    public async Task<WebhookRegistration?> HandleAsync(ApiClientId clientId, CancellationToken cancellationToken) =>
        ToRegistration(await clients.FindByIdAsync(clientId, cancellationToken));

    /// <summary>The HA integration's registration, the one events go to.</summary>
    public async Task<WebhookRegistration?> ForHomeAssistantAsync(CancellationToken cancellationToken) =>
        ToRegistration(await clients.FindByNameAsync(ApiClientNames.HomeAssistant, cancellationToken));

    private static WebhookRegistration? ToRegistration(ApiClient? client) =>
        client is { HasWebhook: true, WebhookUrl: { } url, WebhookRegisteredAt: { } at } ? new WebhookRegistration(url, at) : null;
}
