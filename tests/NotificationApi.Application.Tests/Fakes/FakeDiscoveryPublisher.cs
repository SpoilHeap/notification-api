using NotificationApi.Application.Common;
using NotificationApi.Application.Discovery;

namespace NotificationApi.Application.Tests.Fakes;

/// <summary>Returns queued results in order (the last one repeats) and records every message.</summary>
internal sealed class FakeDiscoveryPublisher : IDiscoveryPublisher
{
    private readonly Queue<Result<DiscoveryReceipt>> _results = new();
    private Result<DiscoveryReceipt> _last = new DiscoveryReceipt("uuid-1");

    public bool IsConfigured { get; set; } = true;

    public List<DiscoveryMessage> Messages { get; } = [];

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
        if (_results.TryDequeue(out var next))
        {
            _last = next;
        }

        return Task.FromResult(_last);
    }
}
