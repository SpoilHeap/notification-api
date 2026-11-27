using NotificationApi.Application.Discovery;

namespace NotificationApi.Web.Hosting;

/// <summary>Publishes Supervisor discovery once the server is listening. A thin shell around <see cref="PublishDiscovery"/>.</summary>
internal sealed partial class DiscoveryHostedService(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    ILogger<DiscoveryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // HA may call the API as soon as it learns about us, so wait until Kestrel is listening.
            await WaitForApplicationStartedAsync(stoppingToken);

            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PublishDiscovery>().HandleAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // A discovery failure must not stop the add-on; the UI offers manual setup.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogUnexpectedFailure(logger, ex);
        }
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken cancellationToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        {
            await started.Task.WaitAsync(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Supervisor discovery failed unexpectedly.")]
    private static partial void LogUnexpectedFailure(ILogger logger, Exception exception);
}
