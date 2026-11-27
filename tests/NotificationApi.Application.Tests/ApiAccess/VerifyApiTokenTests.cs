using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Tests.Fakes;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.Tests.ApiAccess;

public sealed class VerifyApiTokenTests
{
    private readonly InMemoryApiClients _clients = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly ApiToken _token = ApiToken.Generate();
    private readonly ApiClient _client;

    public VerifyApiTokenTests()
    {
        _client = ApiClient.Create(ApiClientNames.HomeAssistant, _token, "protected", _time.GetUtcNow());
        _clients.Add(ApiClient.Create("other", ApiToken.Generate(), "protected", _time.GetUtcNow()));
        _clients.Add(_client);
    }

    private VerifyApiToken UseCase => new(_clients, _clients, _time, NullLogger<VerifyApiToken>.Instance);

    [Fact]
    public async Task AC11_Valid_token_identifies_the_client_and_records_its_use()
    {
        var result = await UseCase.HandleAsync(_token.Value, TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new AuthenticatedApiClient(_client.Id, ApiClientNames.HomeAssistant));
        _client.LastUsedAt.ShouldBe(_time.GetUtcNow());
        _clients.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Calls_within_a_minute_do_not_write_last_used_again()
    {
        await UseCase.HandleAsync(_token.Value, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(30));

        await UseCase.HandleAsync(_token.Value, TestContext.Current.CancellationToken);

        _clients.Saves.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task AC11_Missing_or_malformed_token_is_invalid(string? presented)
    {
        var result = await UseCase.HandleAsync(presented, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ApiTokenErrors.Invalid);
        _clients.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task AC11_Well_formed_but_unknown_token_is_invalid()
    {
        var result = await UseCase.HandleAsync(ApiToken.Generate().Value, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ApiTokenErrors.Invalid);
        _client.LastUsedAt.ShouldBeNull();
    }
}
