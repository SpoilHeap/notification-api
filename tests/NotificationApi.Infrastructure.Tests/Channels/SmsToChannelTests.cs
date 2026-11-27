using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotificationApi.Application;
using NotificationApi.Application.Channels;
using NotificationApi.Application.Common;
using NotificationApi.Application.Settings;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Settings;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NotificationApi.Infrastructure.Tests.Channels;

/// <summary>
/// Contract tests for SMS.to's <c>POST /sms/send</c> (developers.sms.to, "Send single message") against WireMock:
/// request shape, auth header, success parsing and error/timeout mapping to <see cref="ChannelResult"/> (AC22).
/// </summary>
public sealed class SmsToChannelTests : IDisposable
{
    private const string ApiKey = "smsto-api-key-0123456789";
    private const string Number = "+353861234567";

    private readonly WireMockServer _smsTo = WireMockServer.Start();
    private readonly List<ServiceProvider> _providers = [];
    private readonly SettingsStub _settings = new();
    private readonly LogCapture _logs = new();

    public SmsToChannelTests() => _settings.Configure(ApiKey, "Home");

    private static OutboundMessage Message => new(Number, "Front door", "Opened", "Front door: Opened");

    [Fact]
    public async Task AC22_Posts_to_sms_send_with_the_bearer_key_and_stores_the_message_id()
    {
        Respond(200, """{"message":"Message is queued for sending! Please check report for update","success":true,"message_id":"11ec-832f-a6f3fcfe-9fea-02420a0002ab"}""");

        var result = await SendAsync();

        result.Succeeded.ShouldBeTrue();
        result.ProviderMessageId.ShouldBe("11ec-832f-a6f3fcfe-9fea-02420a0002ab");
        var request = _smsTo.LogEntries.ShouldHaveSingleItem().RequestMessage.ShouldNotBeNull();
        (request.Method, request.Path).ShouldBe(("POST", "/sms/send"));
        request.Headers!["Authorization"].ShouldBe([$"Bearer {ApiKey}"]);
        request.Headers["Content-Type"].ShouldHaveSingleItem().ShouldStartWith("application/json");
        request.Body.ShouldBe("""{"to":"+353861234567","message":"Front door: Opened","sender_id":"Home"}""");
    }

    [Fact]
    public async Task With_no_sender_id_the_field_is_left_out_so_the_account_default_applies()
    {
        _settings.Configure(ApiKey, null);
        Respond(200, """{"success":true,"message_id":"abc"}""");

        (await SendAsync()).Succeeded.ShouldBeTrue();

        using var body = JsonDocument.Parse(_smsTo.LogEntries.Single().RequestMessage!.Body!);
        body.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["to", "message"]);
    }

    [Fact]
    public async Task Credentials_are_read_on_every_send_so_changes_apply_without_a_restart()
    {
        Respond(200, """{"success":true,"message_id":"abc"}""");
        var channel = Channel();

        await channel.SendAsync(Message, TestContext.Current.CancellationToken);
        _settings.Configure("a-brand-new-key-000000", "Other");
        await channel.SendAsync(Message, TestContext.Current.CancellationToken);

        _smsTo.LogEntries.Select(e => e.RequestMessage!.Headers!["Authorization"].Single()).ShouldBe([$"Bearer {ApiKey}", "Bearer a-brand-new-key-000000"]);
    }

    [Fact]
    public async Task Not_configured_fails_without_calling_sms_to()
    {
        _settings.Clear();

        var result = await SendAsync();

        result.Error.ShouldBe("SMS.to is not configured");
        _smsTo.LogEntries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(401, """{"success":false,"message":"Invalid API Key or Token","data":[]}""", "SMS.to answered HTTP 401: Invalid API Key or Token")]
    [InlineData(400, """{"success":false,"message":"Your balance is not enough to send message.","data":[],"errors":null}""", "SMS.to answered HTTP 400: Your balance is not enough to send message.")]
    [InlineData(422, """{"success":false,"message":"The to +353861234567 is invalid."}""", "SMS.to answered HTTP 422: The to +3538****567 is invalid.")]
    [InlineData(500, "<html>oops</html>", "SMS.to answered HTTP 500: Internal Server Error")]
    [InlineData(200, """{"success":false,"message":"Sender ID not allowed"}""", "SMS.to answered HTTP 200: Sender ID not allowed")]
    public async Task Errors_map_to_a_failed_result_with_the_provider_message_and_masked_numbers(int status, string body, string expected)
    {
        Respond(status, body);

        var result = await SendAsync();

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe(expected);
    }

    [Fact]
    public async Task Long_provider_messages_are_truncated()
    {
        Respond(400, JsonSerializer.Serialize(new { success = false, message = new string('x', 1000) }));

        (await SendAsync()).Error!.Length.ShouldBeLessThan(260);
    }

    [Fact]
    public async Task A_success_without_a_message_id_still_counts_as_sent()
    {
        // "Sent" is final (spec §2): retrying an accepted message would send it twice.
        Respond(200, """{"success":true}""");

        var result = await SendAsync();

        result.Succeeded.ShouldBeTrue();
        result.ProviderMessageId.ShouldBeNull();
    }

    [Fact]
    public async Task A_slow_answer_times_out_as_a_failed_attempt()
    {
        _smsTo.Given(Request.Create().WithPath("/sms/send").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(2)));

        var result = await Channel(timeout: "00:00:00.200").SendAsync(Message, TestContext.Current.CancellationToken);

        result.Error.ShouldBe("SMS.to did not answer in time.");
    }

    [Fact]
    public async Task Unreachable_is_a_failed_attempt()
    {
        var result = await Channel(baseUrl: "http://127.0.0.1:1/").SendAsync(Message, TestContext.Current.CancellationToken);

        result.Error!.ShouldStartWith("SMS.to is unreachable:");
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_failure()
    {
        _smsTo.Given(Request.Create().WithPath("/sms/send").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(2)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() => Channel().SendAsync(Message, cts.Token));
    }

    [Fact]
    public async Task AC74_Logs_never_contain_the_full_number_the_message_or_the_key()
    {
        Respond(200, """{"success":true,"message_id":"abc"}""");
        await SendAsync();
        Respond(401, """{"success":false,"message":"Invalid API Key or Token"}""");
        await SendAsync();

        _logs.Entries.ShouldNotBeEmpty();
        _logs.Entries.ShouldAllBe(e => !e.Contains(Number) && !e.Contains(ApiKey) && !e.Contains("Opened") && !e.Contains("/sms/send"));
        _logs.Entries.ShouldContain(e => e.Contains("+3538****567"));
    }

    [Fact]
    public void The_channel_is_registered_for_sms()
    {
        using var scope = Provider().CreateScope();

        scope.ServiceProvider.GetServices<INotificationChannel>().ShouldHaveSingleItem().Type.ShouldBe(ChannelType.Sms);
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _smsTo.Stop();
        _smsTo.Dispose();
    }

    private void Respond(int status, string body)
    {
        _smsTo.Reset();
        _smsTo.Given(Request.Create().WithPath("/sms/send").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));
    }

    private Task<ChannelResult> SendAsync() => Channel().SendAsync(Message, TestContext.Current.CancellationToken);

    private INotificationChannel Channel(string? baseUrl = null, string timeout = "00:00:10")
    {
        var scope = Provider(baseUrl, timeout).CreateScope(); // disposed with the provider
        return scope.ServiceProvider.GetServices<INotificationChannel>().Single(c => c.Type == ChannelType.Sms);
    }

    private ServiceProvider Provider(string? baseUrl = null, string timeout = "00:00:10")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataPath"] = Path.GetTempPath(),
                ["SmsTo:BaseUrl"] = baseUrl ?? _smsTo.Url,
                ["SmsTo:RequestTimeout"] = timeout,
            })
            .Build();
        var provider = new ServiceCollection()
            .AddLogging(l => l.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace))
            .AddApplication()
            .AddInfrastructure(configuration)
            .AddSingleton<ISettingsRepository>(_settings)
            .AddSingleton<ISecretProtector, PlainProtector>()
            .BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private sealed class SettingsStub : ISettingsRepository
    {
        private AppSettings? _row;

        public void Configure(string key, string? senderId)
        {
            _row = AppSettings.CreateDefault(DateTimeOffset.UnixEpoch);
            _row.SetSmsTo(key, null, senderId, DateTimeOffset.UnixEpoch);
        }

        public void Clear() => _row = null;

        public Task<AppSettings?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(_row);

        public void Add(AppSettings settings) => _row = settings;
    }

    private sealed class PlainProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public Result<string> Unprotect(string protectedValue) => protectedValue;
    }

    private sealed class LogCapture : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _entries = new();

        public IReadOnlyCollection<string> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCapture sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
                sink._entries.Enqueue($"{logLevel}: {formatter(state, exception)} [{values}]");
            }
        }
    }
}
