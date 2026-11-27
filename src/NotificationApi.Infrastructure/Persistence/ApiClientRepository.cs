using Microsoft.EntityFrameworkCore;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Infrastructure.Persistence;

internal sealed class ApiClientRepository(NotificationDbContext db) : IApiClientRepository
{
    public Task<ApiClient?> FindByIdAsync(ApiClientId id, CancellationToken cancellationToken) =>
        db.ApiClients.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<ApiClient?> FindByNameAsync(string name, CancellationToken cancellationToken) =>
        db.ApiClients.SingleOrDefaultAsync(c => c.Name == name, cancellationToken);

    public async Task<IReadOnlyList<ApiClient>> ListAsync(CancellationToken cancellationToken) =>
        await db.ApiClients.ToListAsync(cancellationToken);

    public void Add(ApiClient client) => db.ApiClients.Add(client);
}
