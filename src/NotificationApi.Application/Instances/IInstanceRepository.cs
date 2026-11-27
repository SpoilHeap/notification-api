using NotificationApi.Domain.Instances;

namespace NotificationApi.Application.Instances;

/// <summary>The single <see cref="Instance"/> row (spec §7).</summary>
public interface IInstanceRepository
{
    Task<Instance?> GetAsync(CancellationToken cancellationToken);

    void Add(Instance instance);
}
