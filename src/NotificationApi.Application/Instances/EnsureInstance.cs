using Microsoft.Extensions.Logging;
using NotificationApi.Application.Common;
using NotificationApi.Domain.Instances;

namespace NotificationApi.Application.Instances;

/// <summary>
/// Returns this installation's <see cref="Instance"/>, creating it the first time (on startup, right after the
/// database is created). After that the instance id never changes (spec §4.2, AC44).
/// </summary>
public sealed partial class EnsureInstance(
    IInstanceRepository instances,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<EnsureInstance> logger)
{
    public async Task<Instance> HandleAsync(CancellationToken cancellationToken)
    {
        var instance = await instances.GetAsync(cancellationToken);
        if (instance is not null)
        {
            return instance;
        }

        instance = Instance.Create(time.GetUtcNow());
        instances.Add(instance);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogCreated(logger, instance.Id);
        return instance;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created instance {InstanceId}.")]
    private static partial void LogCreated(ILogger logger, InstanceId instanceId);
}
