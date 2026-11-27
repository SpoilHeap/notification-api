using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Discovery;
using NotificationApi.Application.Tests.Fakes;

namespace NotificationApi.Application.Tests.Discovery;

public sealed class PublishDiscoveryTests
{
    private readonly InMemoryApiClients _clients = new();
    private readonly FakeSecretProtector _protector = new();
    private readonly FakeDiscoveryPublisher _publisher = new();
    private readonly DiscoveryStatusStore _status = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly DiscoveryOptions _options = new()
    {
        Host = "a0d7b954-notification-api",
        Port = 8080,
        MaxAttempts = 4,
        InitialRetryDelay = TimeSpan.FromSeconds(5),
        MaxRetryDelay = TimeSpan.FromSeconds(15),
    };

    private EnsureApiToken EnsureApiToken => new(_clients, _clients, _protector, _time, NullLogger<EnsureApiToken>.Instance);

    private PublishDiscovery UseCase => new(
        EnsureApiToken, _publisher, _status, Options.Create(_options), _time, NullLogger<PublishDiscovery>.Instance);

    [Fact]
    public async Task AC91_Publishes_host_port_and_the_stored_token_as_notification_api()
    {
        var status = await UseCase.HandleAsync(TestContext.Current.CancellationToken);

        var message = _publisher.Messages.ShouldHaveSingleItem();
        message.Service.ShouldBe("notification_api");
        message.Config.Host.ShouldBe("a0d7b954-notification-api");
        message.Config.Port.ShouldBe(8080);
        _clients.Clients.ShouldHaveSingleItem().Accepts(message.Config.Token).ShouldBeTrue();

        status.ShouldBe(DiscoveryStatus.Published("uuid-1", 1, _time.GetUtcNow()));
        _status.Current.ShouldBe(status);
    }

    [Fact]
    public async Task Every_start_republishes_the_same_token()
    {
        await UseCase.HandleAsync(TestContext.Current.CancellationToken);
        await UseCase.HandleAsync(TestContext.Current.CancellationToken);

        _publisher.Messages.Count.ShouldBe(2);
        _publisher.Messages[1].Config.Token.Value.ShouldBe(_publisher.Messages[0].Config.Token.Value);
        _publisher.Messages[1].Config.Host.ShouldBe(_publisher.Messages[0].Config.Host); // identical, so the Supervisor de-duplicates it
    }

    [Fact]
    public async Task Outside_ha_os_discovery_is_skipped_but_the_token_is_still_provisioned()
    {
        _publisher.IsConfigured = false;

        var status = await UseCase.HandleAsync(TestContext.Current.CancellationToken);

        status.State.ShouldBe(DiscoveryState.Skipped);
        status.Error!.ShouldContain("SUPERVISOR_TOKEN");
        _publisher.Messages.ShouldBeEmpty();
        _clients.Clients.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Transient_failures_are_retried_with_exponential_backoff_capped_at_the_maximum()
    {
        _publisher.Returns(
            DiscoveryErrors.Unreachable("connection refused"),
            DiscoveryErrors.Timeout(),
            DiscoveryErrors.ServerError(502, "bad gateway"),
            new DiscoveryReceipt("uuid-4"));
        var start = _time.GetUtcNow();

        var run = UseCase.HandleAsync(TestContext.Current.CancellationToken);
        _status.Current.State.ShouldBe(DiscoveryState.Pending);
        _status.Current.Error!.ShouldContain("connection refused");

        await AdvanceUntilAttemptAsync(2, TimeSpan.FromSeconds(5));
        await AdvanceUntilAttemptAsync(3, TimeSpan.FromSeconds(10));
        await AdvanceUntilAttemptAsync(4, TimeSpan.FromSeconds(15)); // 20 s capped to 15 s
        var status = await run;

        status.ShouldBe(DiscoveryStatus.Published("uuid-4", 4, start + TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Gives_up_after_the_maximum_number_of_attempts()
    {
        _publisher.Returns(DiscoveryErrors.Unreachable("connection refused"));

        var run = UseCase.HandleAsync(TestContext.Current.CancellationToken);
        await AdvanceUntilAttemptAsync(2, TimeSpan.FromSeconds(5));
        await AdvanceUntilAttemptAsync(3, TimeSpan.FromSeconds(10));
        await AdvanceUntilAttemptAsync(4, TimeSpan.FromSeconds(15));
        var status = await run;

        _publisher.Messages.Count.ShouldBe(4);
        status.State.ShouldBe(DiscoveryState.Failed);
        status.Attempts.ShouldBe(4);
        status.Error!.ShouldContain("connection refused");
        _status.Current.ShouldBe(status);
    }

    [Fact]
    public async Task A_rejection_is_not_retried()
    {
        _publisher.Returns(DiscoveryErrors.Rejected(401, "Unauthorized"));

        var status = await UseCase.HandleAsync(TestContext.Current.CancellationToken);

        _publisher.Messages.Count.ShouldBe(1);
        status.State.ShouldBe(DiscoveryState.Failed);
        status.Error!.ShouldContain("401");
    }

    [Fact]
    public async Task Without_a_usable_token_nothing_is_published()
    {
        await EnsureApiToken.HandleAsync(TestContext.Current.CancellationToken);
        _protector.Broken = true;

        var status = await UseCase.HandleAsync(TestContext.Current.CancellationToken);

        _publisher.Messages.ShouldBeEmpty();
        status.State.ShouldBe(DiscoveryState.Failed);
        status.Error.ShouldBe(ApiTokenErrors.Unrecoverable.Message);
    }

    [Fact]
    public async Task Cancellation_during_backoff_stops_the_retries()
    {
        _publisher.Returns(DiscoveryErrors.Unreachable("connection refused"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // the fakes ignore it; the backoff delay doesn't

        await Should.ThrowAsync<OperationCanceledException>(() => UseCase.HandleAsync(cts.Token));
        _publisher.Messages.Count.ShouldBe(1);
    }

    /// <summary>Moves the fake clock past one backoff delay and waits for the next attempt (no real sleeping).</summary>
    private async Task AdvanceUntilAttemptAsync(int attempt, TimeSpan delay)
    {
        _time.Advance(delay - TimeSpan.FromTicks(1));
        await Task.Yield();
        _publisher.Messages.Count.ShouldBe(attempt - 1, "the retry must not fire before its delay");

        _time.Advance(TimeSpan.FromTicks(1));
        for (var i = 0; i < 1000 && _publisher.Messages.Count < attempt; i++)
        {
            await Task.Yield();
        }

        _publisher.Messages.Count.ShouldBe(attempt);
    }
}
