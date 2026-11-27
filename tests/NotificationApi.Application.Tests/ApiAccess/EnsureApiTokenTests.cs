using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Tests.Fakes;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.Tests.ApiAccess;

public sealed class EnsureApiTokenTests
{
    private readonly InMemoryApiClients _clients = new();
    private readonly FakeSecretProtector _protector = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));

    private EnsureApiToken UseCase => new(_clients, _clients, _protector, _time, NullLogger<EnsureApiToken>.Instance);

    [Fact]
    public async Task First_start_generates_and_stores_a_token_for_home_assistant()
    {
        var result = await UseCase.HandleAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var client = _clients.Clients.ShouldHaveSingleItem();
        client.Name.ShouldBe(ApiClientNames.HomeAssistant);
        client.Accepts(result.Value).ShouldBeTrue();
        client.ProtectedToken.ShouldNotContain(result.Value.Value);
        client.CreatedAt.ShouldBe(_time.GetUtcNow());
        _clients.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Later_starts_return_the_same_token_without_writing()
    {
        var first = await UseCase.HandleAsync(TestContext.Current.CancellationToken);

        var second = await UseCase.HandleAsync(TestContext.Current.CancellationToken);

        second.Value.Value.ShouldBe(first.Value.Value);
        _clients.Clients.Count.ShouldBe(1);
        _clients.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Unreadable_token_is_reported_not_replaced()
    {
        await UseCase.HandleAsync(TestContext.Current.CancellationToken);
        _protector.Broken = true;

        var result = await UseCase.HandleAsync(TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ApiTokenErrors.Unrecoverable);
        _clients.Clients.Count.ShouldBe(1); // the integration's existing token keeps working
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData(null)] // replaced below by a valid but different token
    public async Task Decrypted_value_that_does_not_match_the_hash_is_rejected(string? tampered)
    {
        await UseCase.HandleAsync(TestContext.Current.CancellationToken);
        _protector.Override = tampered ?? ApiToken.Generate().Value;

        var result = await UseCase.HandleAsync(TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ApiTokenErrors.Unrecoverable);
    }
}
