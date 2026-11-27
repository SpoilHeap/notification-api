using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NotificationApi.Application.Common;
using NotificationApi.Application.Discovery;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NotificationApi.Web.Tests.Hosting;

public sealed class DiscoveryHostedServiceTests
{
    [Fact]
    public async Task Discovery_is_skipped_without_SUPERVISOR_TOKEN()
    {
        await using var factory = new NotificationApiFactory();
        using var client = factory.CreateClient();

        var status = await factory.Services.WaitForDiscoveryAsync();

        status.State.ShouldBe(DiscoveryState.Skipped);
        factory.Logs.Entries.ShouldContain(e => e.StartsWith("Information", StringComparison.Ordinal) && e.Contains("discovery skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC91_Publishes_the_stored_token_to_the_supervisor_on_start()
    {
        using var supervisor = WireMockServer.Start();
        supervisor
            .Given(Request.Create().WithPath("/discovery").UsingPost().WithHeader("Authorization", "Bearer sup-token"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"result":"ok","data":{"uuid":"abc123"}}"""));
        await using var factory = new NotificationApiFactory();
        await using var configured = factory.WithWebHostBuilder(b => b
            .UseSetting("SUPERVISOR_TOKEN", "sup-token")
            .UseSetting("Supervisor:BaseUrl", supervisor.Url));
        using var client = configured.CreateClient();

        var status = await configured.Services.WaitForDiscoveryAsync();

        status.ShouldBe(status with { State = DiscoveryState.Published, Uuid = "abc123", Attempts = 1 });
        using var body = JsonDocument.Parse(supervisor.LogEntries.ShouldHaveSingleItem().RequestMessage!.Body!);
        body.RootElement.GetProperty("service").GetString().ShouldBe("notification_api");
        var config = body.RootElement.GetProperty("config");
        config.GetProperty("host").GetString().ShouldBe("notification-api-test");
        config.GetProperty("port").GetInt32().ShouldBe(8080);
        config.GetProperty("token").GetString().ShouldBe(await configured.Services.GetApiTokenAsync());

        factory.Logs.Entries.ShouldNotContain(e => e.Contains("sup-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_restart_republishes_the_same_token()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "notification-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var first = await StartAndCaptureTokenAsync(dataPath);
            var second = await StartAndCaptureTokenAsync(dataPath);

            second.ShouldBe(first);
        }
        finally
        {
            Directory.Delete(dataPath, recursive: true);
        }
    }

    [Fact]
    public async Task An_unexpected_failure_is_logged_and_does_not_stop_the_add_on()
    {
        await using var factory = new NotificationApiFactory();
        await using var failing = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<IDiscoveryPublisher>(new RecordingDiscoveryPublisher(_ => throw new InvalidOperationException("boom")))));
        using var client = failing.CreateClient();

        for (var i = 0; i < 1500 && !factory.Logs.Entries.Any(e => e.Contains("failed unexpectedly", StringComparison.Ordinal)); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        factory.Logs.Entries.ShouldContain(e => e.StartsWith("Error", StringComparison.Ordinal) && e.Contains("failed unexpectedly", StringComparison.Ordinal));
        using var health = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<string> StartAndCaptureTokenAsync(string dataPath)
    {
        var publisher = new RecordingDiscoveryPublisher(_ => new DiscoveryReceipt("uuid"));
        await using var factory = new NotificationApiFactory { DataPath = dataPath, DeleteDataOnDispose = false };
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IDiscoveryPublisher>(publisher)));
        using var client = app.CreateClient();

        (await app.Services.WaitForDiscoveryAsync()).State.ShouldBe(DiscoveryState.Published);
        return publisher.Messages.ShouldHaveSingleItem().Config.Token.Value;
    }

    private sealed class RecordingDiscoveryPublisher(Func<DiscoveryMessage, Result<DiscoveryReceipt>> respond) : IDiscoveryPublisher
    {
        public List<DiscoveryMessage> Messages { get; } = [];

        public bool IsConfigured => true;

        public Task<Result<DiscoveryReceipt>> PublishAsync(DiscoveryMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(respond(message));
        }
    }
}
