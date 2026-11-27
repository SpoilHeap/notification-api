using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Tests.Fakes;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Tests.ApiAccess;

public sealed class RevealApiTokenTests
{
    private readonly InMemoryApiClients _clients = new();
    private readonly FakeSecretProtector _protector = new();

    private readonly InMemoryAuditLog _audit = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 16, 15, 20, TimeSpan.Zero));

    private RevealApiToken UseCase => new(
        new EnsureApiToken(_clients, _clients, _protector, _time, NullLogger<EnsureApiToken>.Instance),
        new AuditTrail(_audit, _time),
        _clients,
        NullLogger<RevealApiToken>.Instance);

    [Fact]
    public async Task Returns_the_stored_token()
    {
        var first = await UseCase.HandleAsync(RecipientsHarness.Actor, TestContext.Current.CancellationToken);
        var second = await UseCase.HandleAsync(RecipientsHarness.Actor, TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        second.Value.Value.ShouldBe(first.Value.Value);
        _clients.Clients[0].Accepts(first.Value).ShouldBeTrue();
    }

    [Fact]
    public async Task Reports_an_unrecoverable_token()
    {
        await UseCase.HandleAsync(RecipientsHarness.Actor, TestContext.Current.CancellationToken);
        _protector.Broken = true;

        var result = await UseCase.HandleAsync(RecipientsHarness.Actor, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ApiTokenErrors.Unrecoverable);
    }

    [Fact]
    public async Task AC82_Every_reveal_is_audited_without_the_token()
    {
        var token = await UseCase.HandleAsync(RecipientsHarness.Actor, TestContext.Current.CancellationToken);

        var entry = _audit.Entries.ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditAction.Reveal);
        entry.EntityType.ShouldBe(AuditEntityType.ApiToken);
        entry.HaUserId.ShouldBe(RecipientsHarness.Actor.HaUserId);
        entry.HaUserDisplayName.ShouldBe(RecipientsHarness.Actor.DisplayName);
        entry.OccurredAt.ShouldBe(_time.GetUtcNow());
        entry.Summary.ShouldNotContain(token.Value.Value);
        _clients.Saves.ShouldBe(2); // token creation, then the audit entry
    }

    [Fact]
    public async Task A_failed_reveal_is_not_audited()
    {
        await UseCase.HandleAsync(RecipientsHarness.Actor, TestContext.Current.CancellationToken);
        _protector.Broken = true;
        _audit.Entries.Clear();

        await UseCase.HandleAsync(RecipientsHarness.Actor, TestContext.Current.CancellationToken);

        _audit.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Requires_the_ha_user() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase.HandleAsync(null!, TestContext.Current.CancellationToken));
}
