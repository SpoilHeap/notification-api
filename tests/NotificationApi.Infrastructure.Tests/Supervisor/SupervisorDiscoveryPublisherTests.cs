using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NotificationApi.Application.Discovery;
using NotificationApi.Domain.ApiClients;
using NotificationApi.Infrastructure.Supervisor;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NotificationApi.Infrastructure.Tests.Supervisor;

/// <summary>Contract tests for <c>POST http://supervisor/discovery</c> against WireMock (spec §9.4, spike 1).</summary>
public sealed class SupervisorDiscoveryPublisherTests : IDisposable
{
    private const string SupervisorToken = "supervisor-token-123";

    private readonly WireMockServer _supervisor = WireMockServer.Start();
    private readonly ApiToken _apiToken = ApiToken.Generate();
    private readonly List<ServiceProvider> _providers = [];

    private DiscoveryMessage Message => new("notification_api", new DiscoveryConfig("a0d7b954-notification-api", 8080, _apiToken));

    [Fact]
    public async Task AC91_Posts_the_discovery_message_with_the_supervisor_token_and_returns_the_uuid()
    {
        _supervisor
            .Given(Request.Create().WithPath("/discovery").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"result":"ok","data":{"uuid":"5e5c4b38f1e44c0bbd5c3c9c7d0b0a55"}}"""));

        var result = await Publisher().PublishAsync(Message, TestContext.Current.CancellationToken);

        result.Value.Uuid.ShouldBe("5e5c4b38f1e44c0bbd5c3c9c7d0b0a55");
        var request = _supervisor.LogEntries.ShouldHaveSingleItem().RequestMessage.ShouldNotBeNull();
        request.Path.ShouldBe("/discovery");
        request.Method.ShouldBe("POST");
        request.Headers!["Authorization"].ShouldBe([$"Bearer {SupervisorToken}"]);
        request.Headers["Content-Type"].ShouldHaveSingleItem().ShouldStartWith("application/json");

        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.ToString().ShouldBe(JsonSerializer.Serialize(new
        {
            service = "notification_api",
            config = new { host = "a0d7b954-notification-api", port = 8080, token = _apiToken.Value },
        }));
    }

    [Theory]
    [InlineData(401, DiscoveryErrors.RejectedCode)]
    [InlineData(403, DiscoveryErrors.RejectedCode)]
    [InlineData(400, DiscoveryErrors.RejectedCode)]
    [InlineData(408, DiscoveryErrors.ServerErrorCode)]
    [InlineData(429, DiscoveryErrors.ServerErrorCode)]
    [InlineData(500, DiscoveryErrors.ServerErrorCode)]
    [InlineData(502, DiscoveryErrors.ServerErrorCode)]
    public async Task Error_statuses_map_to_retryable_or_permanent_errors(int status, string code)
    {
        _supervisor
            .Given(Request.Create().WithPath("/discovery").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status).WithBody("""{"result":"error","message":"nope"}"""));

        var result = await Publisher().PublishAsync(Message, TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(code);
        result.Error.Message.ShouldContain(status.ToString(System.Globalization.CultureInfo.InvariantCulture));
        result.Error.Message.ShouldContain("nope");
    }

    [Fact]
    public async Task Long_error_bodies_are_truncated()
    {
        _supervisor
            .Given(Request.Create().WithPath("/discovery").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody(new string('x', 5000)));

        var result = await Publisher().PublishAsync(Message, TestContext.Current.CancellationToken);

        result.Error.Message.Length.ShouldBeLessThan(400);
    }

    [Theory]
    [InlineData("""{"result":"ok","data":{}}""")]
    [InlineData("""{"result":"ok"}""")]
    [InlineData("null")]
    [InlineData("<html>not json</html>")]
    public async Task A_success_without_a_uuid_is_an_invalid_response(string body)
    {
        _supervisor
            .Given(Request.Create().WithPath("/discovery").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body));

        var result = await Publisher().PublishAsync(Message, TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(DiscoveryErrors.InvalidResponseCode);
    }

    [Fact]
    public async Task A_slow_supervisor_times_out()
    {
        _supervisor
            .Given(Request.Create().WithPath("/discovery").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(1)));

        var result = await Publisher(requestTimeout: "00:00:00.200").PublishAsync(Message, TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(DiscoveryErrors.TimeoutCode);
    }

    [Fact]
    public async Task An_unreachable_supervisor_is_reported()
    {
        var result = await Publisher(baseUrl: "http://127.0.0.1:1/").PublishAsync(Message, TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(DiscoveryErrors.UnreachableCode);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_timeout()
    {
        _supervisor
            .Given(Request.Create().WithPath("/discovery").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(1)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() => Publisher().PublishAsync(Message, cts.Token));
    }

    [Fact]
    public void Supervisor_token_comes_from_the_SUPERVISOR_TOKEN_environment_variable()
    {
        Publisher().IsConfigured.ShouldBeTrue();
        Publisher(supervisorToken: null).IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public async Task Publishing_without_a_supervisor_token_is_a_programming_error() =>
        await Should.ThrowAsync<InvalidOperationException>(() =>
            Publisher(supervisorToken: null).PublishAsync(Message, TestContext.Current.CancellationToken));

    [Theory]
    [InlineData("ftp://supervisor/")]
    [InlineData("/relative")]
    public void An_invalid_base_url_fails_validation(string baseUrl)
    {
        var provider = Provider(baseUrl, SupervisorToken, "00:00:15");

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SupervisorOptions>>().Value)
            .Message.ShouldContain("Supervisor:BaseUrl");
    }

    [Fact]
    public void A_non_positive_timeout_fails_validation()
    {
        var provider = Provider(_supervisor.Url!, SupervisorToken, "00:00:00");

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SupervisorOptions>>().Value)
            .Message.ShouldContain("Supervisor:RequestTimeout");
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _supervisor.Stop();
        _supervisor.Dispose();
    }

    private IDiscoveryPublisher Publisher(string? baseUrl = null, string? supervisorToken = SupervisorToken, string requestTimeout = "00:00:10") =>
        Provider(baseUrl ?? _supervisor.Url!, supervisorToken, requestTimeout).GetRequiredService<IDiscoveryPublisher>();

    private ServiceProvider Provider(string baseUrl, string? supervisorToken, string requestTimeout)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataPath"] = Path.GetTempPath(),
                ["Supervisor:BaseUrl"] = baseUrl,
                ["Supervisor:RequestTimeout"] = requestTimeout,
                [SupervisorOptions.TokenEnvironmentVariable] = supervisorToken,
            })
            .Build();
        var provider = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }
}
