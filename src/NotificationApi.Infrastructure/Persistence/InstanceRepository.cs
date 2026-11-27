using Microsoft.EntityFrameworkCore;
using NotificationApi.Application.Instances;
using NotificationApi.Domain.Instances;

namespace NotificationApi.Infrastructure.Persistence;

internal sealed class InstanceRepository(NotificationDbContext db) : IInstanceRepository
{
    /// <summary>Throws if the table somehow holds more than one row: that is corruption, not a normal state.</summary>
    public Task<Instance?> GetAsync(CancellationToken cancellationToken) => db.Instances.SingleOrDefaultAsync(cancellationToken);

    public void Add(Instance instance) => db.Instances.Add(instance);
}
