using NotificationApi.Application.Common;
using NotificationApi.Application.Instances;
using NotificationApi.Domain.Instances;

namespace NotificationApi.Application.Tests.Fakes;

/// <summary>The instance row and a unit of work. <see cref="Saves"/> counts commits.</summary>
internal sealed class InMemoryInstances : IInstanceRepository, IUnitOfWork
{
    public List<Instance> Rows { get; } = [];

    public int Saves { get; private set; }

    public Task<Instance?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Rows.SingleOrDefault());

    public void Add(Instance instance) => Rows.Add(instance);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
