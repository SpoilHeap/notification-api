using Microsoft.Extensions.Logging;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Common;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.Webhooks;

/// <summary><c>DELETE /webhook</c> (spec §4.3): stops events. Idempotent.</summary>
public sealed partial class RemoveWebhook(IApiClientRepository clients, IUnitOfWork unitOfWork, ILogger<RemoveWebhook> logger)
{
    public async Task HandleAsync(ApiClientId clientId, CancellationToken cancellationToken)
    {
        var client = await clients.FindByIdAsync(clientId, cancellationToken);
        if (client is null || !client.RemoveWebhook())
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogRemoved(logger, client.Name);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "API client '{ClientName}' removed its webhook.")]
    private static partial void LogRemoved(ILogger logger, string clientName);
}
