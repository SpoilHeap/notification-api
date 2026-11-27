using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Discovery;

namespace NotificationApi.Web.Tests;

internal static class TestRequests
{
    public static HttpRequestMessage Api(HttpMethod method, string path, string? bearerToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(NotificationApiFactory.RemoteIpHeader, "172.30.33.4"); // HA Core on the hassio network
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return request;
    }

    /// <summary>
    /// Waits for the discovery hosted service to finish. It runs in the background after startup, so poll
    /// (bounded) rather than sleep for a fixed time.
    /// </summary>
    public static async Task<DiscoveryStatus> WaitForDiscoveryAsync(this IServiceProvider services)
    {
        var store = services.GetRequiredService<DiscoveryStatusStore>();
        for (var i = 0; i < 1500 && store.Current.State == DiscoveryState.Pending; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return store.Current;
    }

    /// <summary>The plaintext API token, as the HA integration receives it through discovery.</summary>
    public static async Task<string> GetApiTokenAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var token = await scope.ServiceProvider.GetRequiredService<EnsureApiToken>().HandleAsync(CancellationToken.None);
        return token.Value.Value;
    }

    /// <summary>A valid webhook secret: standard base64 of 32 random bytes, as the integration generates it.</summary>
    public static string NewWebhookSecret() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary><c>PUT /api/v1/webhook</c> as the integration sends it.</summary>
    public static async Task<HttpResponseMessage> PutWebhookAsync(this HttpClient client, string token, string url, string secret)
    {
        using var request = Api(HttpMethod.Put, "/api/v1/webhook", token);
        request.Content = JsonContent.Create(new { url, secret });
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Runs a use case in its own scope, as an endpoint or hosted service would.</summary>
    public static async Task<T> InScopeAsync<TService, T>(this IServiceProvider services, Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
