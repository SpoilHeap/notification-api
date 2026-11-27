using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NotificationApi.Application.Common;
using NotificationApi.Application.Discovery;
using Polly.Timeout;

namespace NotificationApi.Infrastructure.Supervisor;

/// <summary>
/// <c>POST {BaseUrl}/discovery</c> with <c>Authorization: Bearer $SUPERVISOR_TOKEN</c>. The Supervisor answers
/// <c>{"result": "ok", "data": {"uuid": "..."}}</c> (verified on HA OS in spike 1, spec §13).
/// </summary>
internal sealed class SupervisorDiscoveryPublisher(HttpClient http, IOptions<SupervisorOptions> options) : IDiscoveryPublisher
{
    private const int MaxErrorBodyLength = 300;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.Token);

    public async Task<Result<DiscoveryReceipt>> PublishAsync(DiscoveryMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsConfigured)
        {
            throw new InvalidOperationException("The Supervisor token is not configured. Check IsConfigured first.");
        }

        var body = new DiscoveryRequest(message.Service, new DiscoveryRequestConfig(message.Config.Host, message.Config.Port, message.Config.Token.Value));
        using var request = new HttpRequestMessage(HttpMethod.Post, "discovery")
        {
            Content = JsonContent.Create(body, options: JsonSerializerOptions.Web),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                return IsRetryable(response.StatusCode)
                    ? DiscoveryErrors.ServerError(status, Truncate(text))
                    : DiscoveryErrors.Rejected(status, Truncate(text));
            }

            var uuid = JsonSerializer.Deserialize<SupervisorResponse>(text, JsonSerializerOptions.Web)?.Data?.Uuid;
            return string.IsNullOrWhiteSpace(uuid)
                ? DiscoveryErrors.InvalidResponse("no data.uuid in the response.")
                : new DiscoveryReceipt(uuid);
        }
        catch (TimeoutRejectedException)
        {
            return DiscoveryErrors.Timeout();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DiscoveryErrors.Timeout(); // HttpClient.Timeout
        }
        catch (HttpRequestException ex)
        {
            return DiscoveryErrors.Unreachable(ex.Message);
        }
        catch (JsonException ex)
        {
            return DiscoveryErrors.InvalidResponse(ex.Message);
        }
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

    private static string Truncate(string text) =>
        text.Length <= MaxErrorBodyLength ? text : text[..MaxErrorBodyLength] + "…";

    private sealed record DiscoveryRequest(string Service, DiscoveryRequestConfig Config);

    private sealed record DiscoveryRequestConfig(string Host, int Port, string Token);

    private sealed record SupervisorResponse(string? Result, SupervisorResponseData? Data);

    private sealed record SupervisorResponseData(string? Uuid);
}
