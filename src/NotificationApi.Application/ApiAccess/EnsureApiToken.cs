using Microsoft.Extensions.Logging;
using NotificationApi.Application.Common;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.ApiAccess;

/// <summary>
/// Returns the HA integration's API token, generating and storing it on first start.
/// The token is kept hashed (for verification) and encrypted (so discovery can re-publish the same value), see ADR 0012.
/// </summary>
public sealed partial class EnsureApiToken(
    IApiClientRepository clients,
    IUnitOfWork unitOfWork,
    ISecretProtector protector,
    TimeProvider time,
    ILogger<EnsureApiToken> logger)
{
    public async Task<Result<ApiToken>> HandleAsync(CancellationToken cancellationToken)
    {
        var client = await clients.FindByNameAsync(ApiClientNames.HomeAssistant, cancellationToken);
        if (client is null)
        {
            var token = ApiToken.Generate();
            clients.Add(ApiClient.Create(ApiClientNames.HomeAssistant, token, protector.Protect(token.Value), time.GetUtcNow()));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            LogGenerated(logger, ApiClientNames.HomeAssistant);
            return token;
        }

        var plaintext = protector.Unprotect(client.ProtectedToken);
        if (plaintext.IsFailure)
        {
            LogUnrecoverable(logger, client.Name, plaintext.Error.Code);
            return ApiTokenErrors.Unrecoverable;
        }

        // The decrypted value must still match the hash; anything else means the row was tampered with or corrupted.
        if (!ApiToken.TryParse(plaintext.Value, out var stored) || !client.Accepts(stored))
        {
            LogUnrecoverable(logger, client.Name, "api_token.hash_mismatch");
            return ApiTokenErrors.Unrecoverable;
        }

        return stored;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated a new API token for API client '{ClientName}'.")]
    private static partial void LogGenerated(ILogger logger, string clientName);

    [LoggerMessage(Level = LogLevel.Error, Message = "The stored API token for API client '{ClientName}' can't be recovered ({Reason}).")]
    private static partial void LogUnrecoverable(ILogger logger, string clientName, string reason);
}
