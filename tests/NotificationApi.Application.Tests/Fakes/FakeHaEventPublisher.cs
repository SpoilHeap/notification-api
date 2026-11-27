using NotificationApi.Application.Events;

namespace NotificationApi.Application.Tests.Fakes;

/// <summary>Records every event with its target, and answers with <see cref="Result"/>.</summary>
internal sealed class FakeHaEventPublisher : IHaEventPublisher
{
    public List<(WebhookTarget Target, HaEvent Event)> Sent { get; } = [];

    public WebhookSendResult Result { get; set; } = WebhookSendResult.Delivered(200);

    public Task<WebhookSendResult> PublishAsync(WebhookTarget target, HaEvent haEvent, CancellationToken cancellationToken)
    {
        Sent.Add((target, haEvent));
        return Task.FromResult(Result);
    }
}
