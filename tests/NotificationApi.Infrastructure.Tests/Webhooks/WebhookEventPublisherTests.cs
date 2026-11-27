using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.Events;
using NotificationApi.Infrastructure.Webhooks;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NotificationApi.Infrastructure.Tests.Webhooks;

/// <summary>Contract tests for the HA webhook POST (spec §4.4, AC33, AC34, AC37) against WireMock.</summary>
public sealed class WebhookEventPublisherTests : IDisposable
{
    private const string WebhookPath = "/api/webhook/0f1e2d3c4b5a69788796a5b4c3d2e1f0";

    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(32);

    private readonly WireMockServer _ha = WireMockServer.Start();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 16, 15, 20, TimeSpan.Zero));
    private readonly CapturingLoggerProvider _logs = new();
    private readonly List<ServiceProvider> _providers = [];

    private WebhookTarget Target => new(new Uri(_ha.Url + WebhookPath), Secret);

    [Fact]
    public async Task AC37_Posts_the_event_with_a_timestamp_and_a_signature_over_the_exact_body()
    {
        _ha.Given(Request.Create().WithPath(WebhookPath).UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
        var haEvent = HaEvent.WebhookTest(_time);

        var result = await Publisher().PublishAsync(Target, haEvent, TestContext.Current.CancellationToken);

        result.ShouldBe(WebhookSendResult.Delivered(200));
        var request = _ha.LogEntries.ShouldHaveSingleItem().RequestMessage.ShouldNotBeNull();
        request.Method.ShouldBe("POST");
        request.Headers!["Content-Type"].ShouldBe(["application/json"]);
        request.Headers[WebhookSignature.TimestampHeader].ShouldBe(["1795796120"]);

        // Verify as the receiver does: HMAC-SHA256(decoded secret, "<timestamp>." + raw body bytes).
        var body = request.BodyAsBytes.ShouldNotBeNull();
        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Secret, (byte[])[.. "1795796120."u8, .. body]));
        request.Headers[WebhookSignature.SignatureHeader].ShouldBe([expected]);
        Encoding.UTF8.GetString(body).ShouldBe($$$"""{"id":"{{{haEvent.Id}}}","type":"webhook.test","occurred_at":"2026-11-27T16:15:20Z","data":{}}""");
    }

    [Fact]
    public async Task Each_event_is_signed_with_the_current_time()
    {
        _ha.Given(Request.Create().WithPath(WebhookPath).UsingPost()).RespondWith(Response.Create().WithStatusCode(204));
        var publisher = Publisher();

        await publisher.PublishAsync(Target, HaEvent.WebhookTest(_time), TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(90));
        (await publisher.PublishAsync(Target, HaEvent.WebhookTest(_time), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(204);

        _ha.LogEntries.Select(e => e.RequestMessage!.Headers![WebhookSignature.TimestampHeader].Single())
            .ShouldBe(["1795796120", "1795796210"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(302)]
    public async Task AC34_Other_statuses_are_failures_with_the_status(int status)
    {
        _ha.Given(Request.Create().WithPath(WebhookPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Location", "http://elsewhere.invalid/"));

        var result = await Publisher().PublishAsync(Target, HaEvent.WebhookTest(_time), TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.StatusCode.ShouldBe(status);
        result.Error.ShouldBe($"the webhook answered HTTP {status}");
        _ha.LogEntries.Count.ShouldBe(1); // no retry, and redirects aren't followed
    }

    [Fact]
    public async Task AC34_A_slow_webhook_times_out_after_one_attempt()
    {
        _ha.Given(Request.Create().WithPath(WebhookPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(2)));

        // The resilience timeout runs on the registered TimeProvider, so this test uses the real clock.
        var result = await Publisher(requestTimeout: "00:00:00.200", realClock: true)
            .PublishAsync(Target, HaEvent.WebhookTest(_time), TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.StatusCode.ShouldBeNull();
        result.Error.ShouldBe("the webhook did not answer in time");
    }

    [Fact]
    public async Task AC34_An_unreachable_webhook_is_reported()
    {
        var result = await Publisher().PublishAsync(
            new WebhookTarget(new Uri("http://127.0.0.1:1" + WebhookPath), Secret), HaEvent.WebhookTest(_time), TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.Error!.ShouldStartWith("the webhook is unreachable");
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_timeout()
    {
        _ha.Given(Request.Create().WithPath(WebhookPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(2)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() => Publisher().PublishAsync(Target, HaEvent.WebhookTest(_time), cts.Token));
    }

    [Fact]
    public async Task AC74_Nothing_logged_while_sending_contains_the_secret_the_webhook_path_or_the_body()
    {
        _ha.Given(Request.Create().WithPath(WebhookPath).UsingPost()).RespondWith(Response.Create().WithStatusCode(500));
        var haEvent = HaEvent.WebhookTest(_time);

        await Publisher().PublishAsync(Target, haEvent, TestContext.Current.CancellationToken);

        var secretText = Convert.ToBase64String(Secret);
        _logs.Entries.ShouldAllBe(e =>
            !e.Contains(secretText, StringComparison.Ordinal)
            && !e.Contains(WebhookPath, StringComparison.Ordinal)
            && !e.Contains(haEvent.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validates_its_arguments()
    {
        var publisher = Publisher();

        await Should.ThrowAsync<ArgumentNullException>(() => publisher.PublishAsync(null!, HaEvent.WebhookTest(_time), TestContext.Current.CancellationToken));
        await Should.ThrowAsync<ArgumentNullException>(() => publisher.PublishAsync(Target, null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_timeout_defaults_to_5_seconds_and_must_be_positive()
    {
        new WebhookOptions().RequestTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        Should.Throw<OptionsValidationException>(() => Provider("00:00:00").GetRequiredService<IOptions<WebhookOptions>>().Value)
            .Message.ShouldContain("Webhook:RequestTimeout");
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _ha.Stop();
        _ha.Dispose();
    }

    private IHaEventPublisher Publisher(string requestTimeout = "00:00:05", bool realClock = false) =>
        Provider(requestTimeout, realClock).GetRequiredService<IHaEventPublisher>();

    private ServiceProvider Provider(string requestTimeout, bool realClock = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataPath"] = Path.GetTempPath(),
                ["Webhook:RequestTimeout"] = requestTimeout,
            })
            .Build();
        var provider = new ServiceCollection()
            .AddLogging(l => l.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs))
            .AddSingleton(realClock ? TimeProvider.System : _time)
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
        }
    }
}
