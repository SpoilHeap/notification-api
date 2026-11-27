using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Common;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.Tests.Fakes;

/// <summary>Repository and unit of work over a list. <see cref="Saves"/> counts commits.</summary>
internal sealed class InMemoryApiClients : IApiClientRepository, IUnitOfWork
{
    public List<ApiClient> Clients { get; } = [];

    public int Saves { get; private set; }

    public Task<ApiClient?> FindByIdAsync(ApiClientId id, CancellationToken cancellationToken) =>
        Task.FromResult(Clients.SingleOrDefault(c => c.Id == id));

    public Task<ApiClient?> FindByNameAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(Clients.SingleOrDefault(c => c.Name == name));

    public Task<IReadOnlyList<ApiClient>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApiClient>>(Clients.ToList());

    public void Add(ApiClient client) => Clients.Add(client);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
