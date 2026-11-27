using NotificationApi.Application.Channels;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Tests.Fakes;

/// <summary>Records every message and answers with <see cref="Answer"/> (default: sent, with a provider id).</summary>
internal sealed class FakeChannel(ChannelType type) : INotificationChannel
{
    public ChannelType Type { get; } = type;

    public List<OutboundMessage> Sent { get; } = [];

    /// <summary>Called with the message and the 1-based call number.</summary>
    public Func<OutboundMessage, int, ChannelResult> Answer { get; set; } = (_, n) => ChannelResult.Sent($"msg-{n}");

    public Task<ChannelResult> SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        Sent.Add(message);
        return Task.FromResult(Answer(message, Sent.Count));
    }
}
