using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.ApiAccess;

public interface IApiClientRepository
{
    Task<ApiClient?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiClient>> ListAsync(CancellationToken cancellationToken);

    void Add(ApiClient client);
}
