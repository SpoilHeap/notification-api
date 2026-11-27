using Microsoft.Extensions.Logging;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Common;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.Webhooks;

/// <summary>
/// <c>PUT /webhook</c> (spec §4.3, AC35, AC36): validates, then replaces the client's registration. The secret is stored
/// encrypted. An invalid request changes nothing, so the previous registration is kept.
/// </summary>
public sealed partial class RegisterWebhook(
    IApiClientRepository clients,
    IUnitOfWork unitOfWork,
    ISecretProtector protector,
    TimeProvider time,
    ILogger<RegisterWebhook> logger)
{
    public async Task<Result<WebhookRegistration>> HandleAsync(ApiClientId clientId, RegisterWebhookCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = RegisterWebhookValidator.Validate(command);
        if (errors.Count > 0)
        {
            LogRejected(logger, errors.Keys);
            return Error.Validation(errors);
        }

        var client = await clients.FindByIdAsync(clientId, cancellationToken);
        if (client is null)
        {
            return WebhookErrors.UnknownClient;
        }

        var url = new Uri(command.Url!, UriKind.Absolute);
        client.RegisterWebhook(url, protector.Protect(command.Secret!), time.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The path holds HA's webhook id, which works like a password for the webhook, so only the origin is logged.
        var origin = url.GetLeftPart(UriPartial.Authority);
        LogRegistered(logger, client.Name, origin);
        return new WebhookRegistration(url, client.WebhookRegisteredAt!.Value);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "API client '{ClientName}' registered its webhook at {Origin}.")]
    private static partial void LogRegistered(ILogger logger, string clientName, string origin);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected a webhook registration: invalid {Fields}.")]
    private static partial void LogRejected(ILogger logger, IEnumerable<string> fields);
}
