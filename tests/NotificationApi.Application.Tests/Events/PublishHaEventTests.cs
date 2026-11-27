using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Events;
using NotificationApi.Application.Tests.Fakes;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.Tests.Events;

public sealed class PublishHaEventTests
{
    private const string Secret = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private static readonly Uri Url = new("http://homeassistant:8123/api/webhook/0123abcd");

    private readonly InMemoryApiClients _clients = new();
    private readonly FakeSecretProtector _protector = new();
    private readonly FakeHaEventPublisher _publisher = new();
    private readonly ListLogger<PublishHaEvent> _logger = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 16, 15, 20, TimeSpan.Zero));

    private PublishHaEvent UseCase => new(_clients, _protector, _publisher, _logger);

    [Fact]
    public async Task AC35_Sends_the_event_to_the_registered_url_keyed_with_the_decoded_secret()
    {
        Register();
        var haEvent = HaEvent.WebhookTest(_time);

        var outcome = await UseCase.HandleAsync(haEvent, TestContext.Current.CancellationToken);

        outcome.ShouldBe(HaEventOutcome.Delivered(haEvent.Id, 200));
        var (target, sent) = _publisher.Sent.ShouldHaveSingleItem();
        sent.ShouldBe(haEvent);
        target.Url.ShouldBe(Url);
        target.Secret.ShouldBe(Convert.FromBase64String(Secret));
        _logger.Entries.ShouldAllBe(e => e.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task Without_a_registration_the_event_is_dropped_and_logged_at_debug()
    {
        _clients.Add(ApiClient.Create(ApiClientNames.HomeAssistant, ApiToken.Generate(), "protected:x", _time.GetUtcNow()));
        var haEvent = HaEvent.WebhookTest(_time);

        var outcome = await UseCase.HandleAsync(haEvent, TestContext.Current.CancellationToken);

        outcome.ShouldBe(HaEventOutcome.NotRegistered(haEvent.Id));
        _publisher.Sent.ShouldBeEmpty();
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Text.ShouldContain(haEvent.Id);
    }

    [Fact]
    public async Task Without_any_api_client_the_event_is_dropped()
    {
        var outcome = await UseCase.HandleAsync(HaEvent.WebhookTest(_time), TestContext.Current.CancellationToken);

        outcome.Kind.ShouldBe(HaEventOutcomeKind.NotRegistered);
    }

    [Fact]
    public async Task AC34_A_failed_post_is_logged_at_warning_with_type_id_and_status_but_never_the_secret()
    {
        Register();
        _publisher.Result = WebhookSendResult.Failed("the webhook answered HTTP 500", 500);
        var haEvent = HaEvent.RecipientsChanged("7", _time);

        var outcome = await UseCase.HandleAsync(haEvent, TestContext.Current.CancellationToken);

        outcome.ShouldBe(HaEventOutcome.Failed(haEvent.Id, "the webhook answered HTTP 500", 500));
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Text.ShouldContain("recipients.changed");
        entry.Text.ShouldContain(haEvent.Id);
        entry.Text.ShouldContain("500");
        entry.Text.ShouldNotContain(Secret);
        entry.Text.ShouldNotContain("0123abcd"); // HA's webhook id, in the URL path
    }

    [Fact]
    public async Task An_unreadable_secret_fails_without_sending()
    {
        Register();
        _protector.Broken = true;

        var outcome = await UseCase.HandleAsync(HaEvent.WebhookTest(_time), TestContext.Current.CancellationToken);

        outcome.Kind.ShouldBe(HaEventOutcomeKind.Failed);
        outcome.Error!.ShouldContain("decrypted");
        _publisher.Sent.ShouldBeEmpty();
        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task A_decrypted_secret_that_is_not_valid_base64_fails_without_sending()
    {
        Register();
        _protector.Override = "not base64!";

        var outcome = await UseCase.HandleAsync(HaEvent.WebhookTest(_time), TestContext.Current.CancellationToken);

        outcome.Kind.ShouldBe(HaEventOutcomeKind.Failed);
        _publisher.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Requires_an_event() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase.HandleAsync(null!, TestContext.Current.CancellationToken));

    private void Register()
    {
        var client = ApiClient.Create(ApiClientNames.HomeAssistant, ApiToken.Generate(), "protected:x", _time.GetUtcNow());
        client.RegisterWebhook(Url, _protector.Protect(Secret), _time.GetUtcNow());
        _clients.Add(client);
    }
}
