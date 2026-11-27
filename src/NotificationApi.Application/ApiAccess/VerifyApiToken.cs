using Microsoft.Extensions.Logging;
using NotificationApi.Application.Common;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.ApiAccess;

/// <summary>Checks a presented bearer token against the stored hashes (constant-time comparison, engineering_standards.md §6).</summary>
public sealed partial class VerifyApiToken(
    IApiClientRepository clients,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<VerifyApiToken> logger)
{
    public async Task<Result<AuthenticatedApiClient>> HandleAsync(string? presentedToken, CancellationToken cancellationToken)
    {
        if (!ApiToken.TryParse(presentedToken, out var token))
        {
            LogRejected(logger, "malformed");
            return ApiTokenErrors.Invalid;
        }

        ApiClient? match = null;
        foreach (var client in await clients.ListAsync(cancellationToken))
        {
            // No early exit: every stored hash is compared, whatever matches.
            if (client.Accepts(token))
            {
                match = client;
            }
        }

        if (match is null)
        {
            LogRejected(logger, "unknown");
            return ApiTokenErrors.Invalid;
        }

        if (match.RecordUse(time.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new AuthenticatedApiClient(match.Id, match.Name);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected an API request: the bearer token is {Reason}.")]
    private static partial void LogRejected(ILogger logger, string reason);
}
