using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Tests.Fakes;

namespace NotificationApi.Application.Tests.ApiAccess;

public sealed class RevealApiTokenTests
{
    private readonly InMemoryApiClients _clients = new();
    private readonly FakeSecretProtector _protector = new();

    private RevealApiToken UseCase => new(
        new EnsureApiToken(_clients, _clients, _protector, new FakeTimeProvider(), NullLogger<EnsureApiToken>.Instance),
        NullLogger<RevealApiToken>.Instance);

    [Fact]
    public async Task Returns_the_stored_token()
    {
        var first = await UseCase.HandleAsync("ha-user-1", TestContext.Current.CancellationToken);
        var second = await UseCase.HandleAsync("ha-user-1", TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        second.Value.Value.ShouldBe(first.Value.Value);
        _clients.Clients[0].Accepts(first.Value).ShouldBeTrue();
    }

    [Fact]
    public async Task Reports_an_unrecoverable_token()
    {
        await UseCase.HandleAsync("ha-user-1", TestContext.Current.CancellationToken);
        _protector.Broken = true;

        var result = await UseCase.HandleAsync("ha-user-1", TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ApiTokenErrors.Unrecoverable);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Requires_the_ha_user(string haUserId) =>
        await Should.ThrowAsync<ArgumentException>(() => UseCase.HandleAsync(haUserId, TestContext.Current.CancellationToken));
}
