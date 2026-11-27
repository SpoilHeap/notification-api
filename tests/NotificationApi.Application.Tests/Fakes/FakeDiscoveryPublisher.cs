using NotificationApi.Application.Common;
using NotificationApi.Application.Discovery;

namespace NotificationApi.Application.Tests.Fakes;

/// <summary>Returns queued results in order (the last one repeats) and records every message.</summary>
internal sealed class FakeDiscoveryPublisher : IDiscoveryPublisher, IDisposable
{
    private readonly Queue<Result<DiscoveryReceipt>> _results = new();
    private readonly SemaphoreSlim _calls = new(0);
    private Result<DiscoveryReceipt> _last = new DiscoveryReceipt("uuid-1");

    public bool IsConfigured { get; set; } = true;

    public List<DiscoveryMessage> Messages { get; } = [];

    /// <summary>Waits for the next call. The timeout is only a safety net against a hung test, not a sleep.</summary>
    public Task<bool> NextCallAsync(CancellationToken cancellationToken) => _calls.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

    public FakeDiscoveryPublisher Returns(params Result<DiscoveryReceipt>[] results)
    {
        foreach (var result in results)
        {
            _results.Enqueue(result);
        }

        return this;
    }

    public Task<Result<DiscoveryReceipt>> PublishAsync(DiscoveryMessage message, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        _calls.Release();
        if (_results.TryDequeue(out var next))
        {
            _last = next;
        }

        return Task.FromResult(_last);
    }

    public void Dispose() => _calls.Dispose();
}
