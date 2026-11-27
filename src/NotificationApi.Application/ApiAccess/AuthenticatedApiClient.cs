using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.ApiAccess;

/// <summary>The caller identified by a valid API token.</summary>
public sealed record AuthenticatedApiClient(ApiClientId Id, string Name);
