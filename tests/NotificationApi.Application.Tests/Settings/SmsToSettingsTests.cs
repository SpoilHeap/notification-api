using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Settings;
using NotificationApi.Application.Tests.Fakes;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Tests.Settings;

/// <summary>The SMS.to part of the Settings page (AC70, AC82): encrypted key, masked display, audited changes.</summary>
public sealed class SmsToSettingsTests
{
    private const string ApiKey = "sk_live_1234567890abcdWXYZ";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 16, 0, 0, TimeSpan.Zero));
    private readonly InMemorySettings _settings = new();
    private readonly FakeSecretProtector _protector = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly Journal _journal = new();
    private readonly ListLogger<SaveSmsToSettings> _log = new();

    private SaveSmsToSettings Save => new(_settings, _protector, _journal, new AuditTrail(_audit, _time), _time, _log);

    private RemoveSmsToSettings Remove => new(_settings, _journal, new AuditTrail(_audit, _time), _time, NullLogger<RemoveSmsToSettings>.Instance);

    private ReadSmsToCredentials Read => new(_settings, _protector);

    private GetSmsToSettings Get => new(_settings);

    [Fact]
    public async Task Nothing_is_configured_at_first()
    {
        (await Get.HandleAsync(CancellationToken.None)).ShouldBe(new SmsToSettingsView(false, null, null));
        (await Read.HandleAsync(CancellationToken.None)).Error.ShouldBe(SmsToErrors.NotConfigured);
        SmsToErrors.NotConfigured.Message.ShouldBe("SMS.to is not configured");
    }

    [Fact]
    public async Task AC70_Saving_stores_the_key_encrypted_and_shows_it_masked()
    {
        var view = await Save.HandleAsync(RecipientsHarness.Actor, new SaveSmsToSettingsCommand($"  {ApiKey} ", " Home "), CancellationToken.None);

        view.Value.ShouldBe(new SmsToSettingsView(true, "••••••••WXYZ", "Home"));
        _settings.Row!.SmsToProtectedApiKey.ShouldNotBeNull().ShouldNotContain(ApiKey);
        (await Read.HandleAsync(CancellationToken.None)).Value.ShouldBe(new SmsToCredentials(ApiKey, "Home"));
        (await Get.HandleAsync(CancellationToken.None)).ShouldBe(view.Value);
        _journal.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task A_short_key_is_masked_completely()
    {
        var view = await Save.HandleAsync(RecipientsHarness.Actor, new SaveSmsToSettingsCommand("short", null), CancellationToken.None);

        view.Value.MaskedApiKey.ShouldBe("••••••••");
    }

    [Fact]
    public async Task AC82_Changes_are_audited_without_the_key()
    {
        await Save.HandleAsync(RecipientsHarness.Actor, new SaveSmsToSettingsCommand(ApiKey, null), CancellationToken.None);
        await Save.HandleAsync(RecipientsHarness.Actor, new SaveSmsToSettingsCommand(null, "Home"), CancellationToken.None);
        await Save.HandleAsync(RecipientsHarness.Actor, new SaveSmsToSettingsCommand("another-key-0000", "Home"), CancellationToken.None);
        await Remove.HandleAsync(RecipientsHarness.Actor, CancellationToken.None);

        _audit.Entries.Select(e => (e.Action, e.EntityType, e.EntityKey, e.Summary)).ShouldBe(
        [
            (AuditAction.Create, AuditEntityType.Settings, "sms_to", "SMS.to: API key set"),
            (AuditAction.Update, AuditEntityType.Settings, "sms_to", "SMS.to: sender ID set to 'Home'"),
            (AuditAction.Update, AuditEntityType.Settings, "sms_to", "SMS.to: API key replaced"),
            (AuditAction.Delete, AuditEntityType.Settings, "sms_to", "SMS.to: API key and sender ID removed"),
        ]);
        _audit.Entries.ShouldAllBe(e => e.HaUserId == RecipientsHarness.Actor.HaUserId);
        _log.Entries.ShouldAllBe(e => !e.Text.Contains(ApiKey));
        (await Read.HandleAsync(CancellationToken.None)).Error.ShouldBe(SmsToErrors.NotConfigured);
    }

    [Fact]
    public async Task A_blank_key_keeps_the_stored_one_and_an_unchanged_save_writes_nothing()
    {
        await Save.HandleAsync(RecipientsHarness.Actor, new SaveSmsToSettingsCommand(ApiKey, "Home"), CancellationToken.None);

        var same = await Save.HandleAsync(RecipientsHarness.Actor, new SaveSmsToSettingsCommand("", "Home"), CancellationToken.None);

        same.Value.Configured.ShouldBeTrue();
        _audit.Entries.Count.ShouldBe(1);
        _journal.Saves.ShouldBe(1);
        (await Read.HandleAsync(CancellationToken.None)).Value.ApiKey.ShouldBe(ApiKey);
    }

    [Theory]
    [InlineData(null, null, SmsToFields.ApiKey)]
    [InlineData("has space", null, SmsToFields.ApiKey)]
    [InlineData("key", "Far too long name", SmsToFields.SenderId)]
    [InlineData("key", "bad!", SmsToFields.SenderId)]
    [InlineData("key", "+1234567890123456", SmsToFields.SenderId)]
    public async Task Invalid_input_is_a_field_error_and_saves_nothing(string? key, string? sender, string field)
    {
        var result = await Save.HandleAsync(RecipientsHarness.Actor, new SaveSmsToSettingsCommand(key, sender), CancellationToken.None);

        result.Error.Code.ShouldBe(Error.ValidationCode);
        result.Error.FieldErrors.Keys.ShouldBe([field]);
        _settings.Row.ShouldBeNull();
        _audit.Entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("SMSto 2026")]
    [InlineData("+447700900123")]
    [InlineData("447700900123")]
    public async Task Valid_sender_ids_are_accepted(string sender)
    {
        (await Save.HandleAsync(RecipientsHarness.Actor, new SaveSmsToSettingsCommand("key", sender), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unreadable_key_is_reported_not_thrown()
    {
        await Save.HandleAsync(RecipientsHarness.Actor, new SaveSmsToSettingsCommand(ApiKey, null), CancellationToken.None);
        _protector.Broken = true;

        (await Read.HandleAsync(CancellationToken.None)).Error.ShouldBe(SmsToErrors.UnreadableKey);
    }

    [Fact]
    public async Task Removing_nothing_returns_false()
    {
        (await Remove.HandleAsync(RecipientsHarness.Actor, CancellationToken.None)).ShouldBeFalse();
        _audit.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Credentials_and_commands_never_print_the_key()
    {
        new SmsToCredentials(ApiKey, "Home").ToString().ShouldNotContain(ApiKey);
        new SaveSmsToSettingsCommand(ApiKey, "Home").ToString().ShouldNotContain(ApiKey);
    }
}
