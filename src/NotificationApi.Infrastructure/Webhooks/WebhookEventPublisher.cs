using System.Net.Http.Headers;
using NotificationApi.Application.Events;
using Polly.Timeout;

namespace NotificationApi.Infrastructure.Webhooks;

/// <summary>
/// POSTs a signed event to the HA webhook (spec §4.4). The body is serialised once and those exact bytes are both
/// signed and sent. One attempt: the HTTP pipeline has a timeout and no retry (AC34). Redirects aren't followed.
/// </summary>
internal sealed class WebhookEventPublisher(HttpClient http, TimeProvider time) : IHaEventPublisher
{
    private static readonly MediaTypeHeaderValue Json = new("application/json");

    public async Task<WebhookSendResult> PublishAsync(WebhookTarget target, HaEvent haEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(haEvent);

        var body = HaEventSerializer.Serialize(haEvent);
        var timestamp = time.GetUtcNow().ToUnixTimeSeconds();

        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = Json;
        using var request = new HttpRequestMessage(HttpMethod.Post, target.Url) { Content = content };
        request.Headers.Add(WebhookSignature.TimestampHeader, WebhookSignature.FormatTimestamp(timestamp));
        request.Headers.Add(WebhookSignature.SignatureHeader, WebhookSignature.Compute(target.Secret, timestamp, body));

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? WebhookSendResult.Delivered(status)
                : WebhookSendResult.Failed($"the webhook answered HTTP {status}", status);
        }
        catch (TimeoutRejectedException)
        {
            return WebhookSendResult.Failed("the webhook did not answer in time");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WebhookSendResult.Failed("the webhook did not answer in time"); // HttpClient.Timeout
        }
        catch (HttpRequestException ex)
        {
            // The message names the host and port at most, never the path, the secret or the body.
            return WebhookSendResult.Failed($"the webhook is unreachable: {ex.Message}");
        }
    }
}
