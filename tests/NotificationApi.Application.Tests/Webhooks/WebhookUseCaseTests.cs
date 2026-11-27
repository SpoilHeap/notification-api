using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Events;
using NotificationApi.Application.Tests.Fakes;
using NotificationApi.Application.Webhooks;
using NotificationApi.Domain.ApiClients;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Tests.Webhooks;

public sealed class WebhookUseCaseTests
{
    private const string Secret = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private const string Url = "http://homeassistant:8123/api/webhook/first";

    private readonly InMemoryApiClients _clients = new();
    private readonly FakeSecretProtector _protector = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 16, 15, 20, TimeSpan.Zero));
    private readonly ListLogger<RegisterWebhook> _registerLogger = new();
    private readonly ApiClient _client;

    public WebhookUseCaseTests()
    {
        _client = ApiClient.Create(ApiClientNames.HomeAssistant, ApiToken.Generate(), "protected:x", _time.GetUtcNow());
        _clients.Add(_client);
    }

    private RegisterWebhook Register => new(_clients, _clients, _protector, _time, _registerLogger);

    private GetWebhook Get => new(_clients);

    private RemoveWebhook Remove => new(_clients, _clients, NullLogger<RemoveWebhook>.Instance);

    [Fact]
    public async Task AC35_Registering_stores_the_url_and_the_encrypted_secret()
    {
        var result = await Register.HandleAsync(_client.Id, new RegisterWebhookCommand(Url, Secret), TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new WebhookRegistration(new Uri(Url), _time.GetUtcNow()));
        _client.WebhookUrl.ShouldBe(new Uri(Url));
        _client.ProtectedWebhookSecret.ShouldBe(_protector.Protect(Secret));
        _client.ProtectedWebhookSecret!.ShouldNotContain(Secret, Case.Sensitive);
        _clients.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task AC35_A_second_registration_replaces_the_first()
    {
        await Register.HandleAsync(_client.Id, new RegisterWebhookCommand(Url, Secret), TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(5));
        var secondSecret = Convert.ToBase64String(new byte[40]);

        await Register.HandleAsync(_client.Id, new RegisterWebhookCommand("https://ha.example/api/webhook/second", secondSecret), TestContext.Current.CancellationToken);

        var registration = (await Get.HandleAsync(_client.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        registration.Url.ShouldBe(new Uri("https://ha.example/api/webhook/second"));
        registration.RegisteredAt.ShouldBe(_time.GetUtcNow());
        _client.ProtectedWebhookSecret.ShouldBe(_protector.Protect(secondSecret));
    }

    [Fact]
    public async Task AC36_An_invalid_registration_keeps_the_previous_one()
    {
        await Register.HandleAsync(_client.Id, new RegisterWebhookCommand(Url, Secret), TestContext.Current.CancellationToken);

        var result = await Register.HandleAsync(_client.Id, new RegisterWebhookCommand("/relative", "c2hvcnQ="), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(Error.ValidationCode);
        result.Error.FieldErrors.Keys.ShouldBe(["url", "secret"], ignoreOrder: true);
        _client.WebhookUrl.ShouldBe(new Uri(Url));
        _client.ProtectedWebhookSecret.ShouldBe(_protector.Protect(Secret));
        _clients.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Logs_never_contain_the_secret_or_the_webhook_path()
    {
        await Register.HandleAsync(_client.Id, new RegisterWebhookCommand(Url, Secret), TestContext.Current.CancellationToken);
        await Register.HandleAsync(_client.Id, new RegisterWebhookCommand("nope", Secret), TestContext.Current.CancellationToken);

        _registerLogger.Entries.Count.ShouldBe(2);
        _registerLogger.Entries[0].Text.ShouldContain("http://homeassistant:8123");
        _registerLogger.Entries.ShouldAllBe(e => !e.Text.Contains(Secret) && !e.Text.Contains("/api/webhook/first"));
        _registerLogger.Entries[1].Text.ShouldContain("url");
    }

    [Fact]
    public async Task Registering_for_an_unknown_client_fails()
    {
        var result = await Register.HandleAsync(ApiClientId.New(), new RegisterWebhookCommand(Url, Secret), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(WebhookErrors.UnknownClient);
    }

    [Fact]
    public async Task Get_returns_null_without_a_registration_and_never_the_secret()
    {
        (await Get.HandleAsync(_client.Id, TestContext.Current.CancellationToken)).ShouldBeNull();
        (await Get.ForHomeAssistantAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
        (await Get.HandleAsync(ApiClientId.New(), TestContext.Current.CancellationToken)).ShouldBeNull();

        await Register.HandleAsync(_client.Id, new RegisterWebhookCommand(Url, Secret), TestContext.Current.CancellationToken);

        var registration = (await Get.ForHomeAssistantAsync(TestContext.Current.CancellationToken)).ShouldNotBeNull();
        registration.ToString().ShouldNotContain(Secret);
    }

    [Fact]
    public async Task AC35_Remove_clears_the_registration_and_is_idempotent()
    {
        await Register.HandleAsync(_client.Id, new RegisterWebhookCommand(Url, Secret), TestContext.Current.CancellationToken);

        await Remove.HandleAsync(_client.Id, TestContext.Current.CancellationToken);
        await Remove.HandleAsync(_client.Id, TestContext.Current.CancellationToken);
        await Remove.HandleAsync(ApiClientId.New(), TestContext.Current.CancellationToken);

        (await Get.HandleAsync(_client.Id, TestContext.Current.CancellationToken)).ShouldBeNull();
        _clients.Saves.ShouldBe(2); // the registration and one removal
    }

    [Fact]
    public async Task Register_requires_a_command() =>
        await Should.ThrowAsync<ArgumentNullException>(() => Register.HandleAsync(_client.Id, null!, TestContext.Current.CancellationToken));

    [Fact]
    public async Task Send_test_event_publishes_an_empty_webhook_test_event()
    {
        await Register.HandleAsync(_client.Id, new RegisterWebhookCommand(Url, Secret), TestContext.Current.CancellationToken);
        var publisher = new FakeHaEventPublisher();
        var logger = new ListLogger<SendTestEvent>();
        var useCase = SendTest(publisher, logger, new InMemoryAuditLog());

        var outcome = await useCase.HandleAsync(RecipientsHarness.Actor, TestContext.Current.CancellationToken);

        var sent = publisher.Sent.ShouldHaveSingleItem().Event;
        sent.Type.ShouldBe(HaEventTypes.WebhookTest);
        sent.Data.ShouldBeOfType<WebhookTestData>();
        outcome.ShouldBe(HaEventOutcome.Delivered(sent.Id, 200));
        logger.Entries.ShouldHaveSingleItem().Text.ShouldContain(RecipientsHarness.Actor.HaUserId);
        await Should.ThrowAsync<ArgumentNullException>(() => useCase.HandleAsync(null!, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true, null, "Sent a webhook.test event: delivered (HTTP 200)")]
    [InlineData(true, 500, "Sent a webhook.test event: failed (HTTP 500)")]
    [InlineData(true, 0, "Sent a webhook.test event: failed (no response)")]
    [InlineData(false, null, "Sent a webhook.test event: not sent: no webhook is registered")]
    public async Task AC82_Send_test_event_is_audited_with_its_outcome(bool registered, int? failure, string summary)
    {
        if (registered)
        {
            await Register.HandleAsync(_client.Id, new RegisterWebhookCommand(Url, Secret), TestContext.Current.CancellationToken);
        }

        var publisher = new FakeHaEventPublisher();
        if (failure is { } status)
        {
            publisher.Result = status == 0 ? WebhookSendResult.Failed("unreachable") : WebhookSendResult.Failed("bad status", status);
        }

        var audit = new InMemoryAuditLog();
        var saves = _clients.Saves;

        var outcome = await SendTest(publisher, new ListLogger<SendTestEvent>(), audit).HandleAsync(RecipientsHarness.Actor, TestContext.Current.CancellationToken);

        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditAction.Send);
        entry.EntityType.ShouldBe(AuditEntityType.WebhookEvent);
        entry.EntityKey.ShouldBe(outcome.EventId);
        entry.Summary.ShouldBe(summary);
        entry.HaUserId.ShouldBe(RecipientsHarness.Actor.HaUserId);
        _clients.Saves.ShouldBe(saves + 1);
    }

    private SendTestEvent SendTest(FakeHaEventPublisher publisher, ListLogger<SendTestEvent> logger, InMemoryAuditLog audit) =>
        new(new PublishHaEvent(_clients, _protector, publisher, NullLogger<PublishHaEvent>.Instance), new AuditTrail(audit, _time), _clients, _time, logger);
}
