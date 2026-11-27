using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NotificationApi.Application.Channels;
using NotificationApi.Application.Settings;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;
using Polly.Timeout;

namespace NotificationApi.Infrastructure.Channels;

/// <summary>
/// SMS through SMS.to's REST API (spec §6), one attempt per call: <c>POST /sms/send</c> with
/// <c>Authorization: Bearer &lt;api key&gt;</c> and <c>{"to", "message", "sender_id"}</c>. SMS.to answers
/// <c>{"success": true, "message_id": "…", "message": "Message is queued for sending! …"}</c>; that is final
/// ("sent", spec §2) and the <c>message_id</c> is stored (AC22). Errors come back as
/// <c>{"success": false, "message": "…"}</c> with a 4xx status. The credentials are read from the Settings store on
/// every send, so changes apply without a restart. Logs carry masked numbers only, never the body or the key (AC74).
/// </summary>
internal sealed partial class SmsToChannel(HttpClient http, ReadSmsToCredentials credentials, ILogger<SmsToChannel> logger) : INotificationChannel
{
    private const int MaxProviderMessageLength = 200;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // "+353…", not "\u002B353…"
    };

    public ChannelType Type => ChannelType.Sms;

    public async Task<ChannelResult> SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var creds = await credentials.HandleAsync(cancellationToken);
        if (creds.IsFailure)
        {
            return ChannelResult.Failed(creds.Error.Message);
        }

        var masked = PhoneNumber.TryParse(message.Address, out var number) ? number.Masked : "(invalid number)";
        using var request = new HttpRequestMessage(HttpMethod.Post, "sms/send")
        {
            Content = JsonContent.Create(new SendRequest(message.Address, message.Text, creds.Value.SenderId), options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.Value.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var answer = await ReadAnswerAsync(response, cancellationToken);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode && answer?.Success != false)
            {
                LogAccepted(logger, masked, answer?.MessageId);
                return ChannelResult.Sent(string.IsNullOrWhiteSpace(answer?.MessageId) ? null : answer.MessageId);
            }

            var reason = Clean(answer?.Message) ?? response.ReasonPhrase ?? "no details";
            LogRejected(logger, masked, status);
            return ChannelResult.Failed($"SMS.to answered HTTP {status}: {reason}");
        }
        catch (TimeoutRejectedException)
        {
            return ChannelResult.Failed("SMS.to did not answer in time.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ChannelResult.Failed("SMS.to did not answer in time."); // HttpClient.Timeout
        }
        catch (HttpRequestException ex)
        {
            return ChannelResult.Failed($"SMS.to is unreachable: {ex.Message}");
        }
    }

    private static async Task<SendAnswer?> ReadAnswerAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<SendAnswer>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null; // not JSON
        }
    }

    /// <summary>The provider's message is stored and sent to HA, so mask any number in it and keep it short.</summary>
    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var masked = PhoneNumber.MaskAll(text.Trim());
        return masked.Length <= MaxProviderMessageLength ? masked : masked[..MaxProviderMessageLength] + "…";
    }

    private sealed record SendRequest(string To, string Message, string? SenderId);

    private sealed record SendAnswer(bool? Success, string? MessageId, string? Message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "SMS.to accepted the SMS to {MaskedNumber} (message id {MessageId}).")]
    private static partial void LogAccepted(ILogger logger, string maskedNumber, string? messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "SMS.to rejected the SMS to {MaskedNumber} (HTTP {StatusCode}).")]
    private static partial void LogRejected(ILogger logger, string maskedNumber, int statusCode);
}
