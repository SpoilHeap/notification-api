using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationApi.Application.Settings;
using NotificationApi.Infrastructure.Persistence;

namespace NotificationApi.Web.Tests.Components;

/// <summary>The Settings page's SMS.to section (AC70 for SMS, AC82, AC83, AC74).</summary>
public sealed class SettingsPageTests
{
    private const string ApiKey = "smsto-live-key-0123456789abcdef";

    [Fact]
    public async Task The_page_renders_through_ingress_with_a_nav_link_and_says_not_configured()
    {
        await using var factory = new NotificationApiFactory();
        using var ui = UiClient.For(factory);

        var html = await ui.GetAsync("/settings");

        html.ShouldContain("<h1>Settings</h1>");
        html.ShouldContain("href=\"settings\"");
        html.ShouldContain("data-testid=\"smsto-state\">Not configured");
        html.ShouldContain("Account default");
        (await ui.GetAsync("/contacts")).ShouldContain("href=\"settings\"");
    }

    [Fact]
    public async Task AC70_Saving_redirects_shows_the_key_masked_and_stores_it_encrypted()
    {
        await using var factory = new NotificationApiFactory();
        using var ui = UiClient.For(factory);

        using (var response = await ui.PostAsync("/settings", "save-smsto", [KeyValuePair.Create("SmsTo.ApiKey", ApiKey), KeyValuePair.Create("SmsTo.SenderId", "Home")]))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.SeeOther);
            response.Headers.Location!.OriginalString.ShouldBe("/settings?saved=smsto");
        }

        var page = WebUtility.HtmlDecode(await ui.GetAsync("/settings?saved=smsto"));
        page.ShouldContain("SMS.to settings saved.");
        page.ShouldContain("data-testid=\"smsto-state\">Configured");
        page.ShouldContain("<code data-testid=\"smsto-key\">••••••••cdef</code>");
        page.ShouldContain("data-testid=\"smsto-sender\">Home");
        page.ShouldNotContain(ApiKey);

        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database
            .SqlQueryRaw<string>("SELECT SmsToProtectedApiKey AS Value FROM Settings").SingleAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotContain(ApiKey);
        stored.ShouldStartWith("CfDJ8"); // Data Protection payload
        var credentials = await scope.ServiceProvider.GetRequiredService<ReadSmsToCredentials>().HandleAsync(TestContext.Current.CancellationToken);
        credentials.Value.ShouldBe(new SmsToCredentials(ApiKey, "Home"));
    }

    [Fact]
    public async Task A_blank_key_keeps_the_stored_one_and_remove_clears_it()
    {
        await using var factory = new NotificationApiFactory();
        using var ui = UiClient.For(factory);
        await ui.SubmitAsync("/settings", "save-smsto", ("SmsTo.ApiKey", ApiKey));

        var page = WebUtility.HtmlDecode(await ui.SubmitAsync("/settings", "save-smsto", ("SmsTo.ApiKey", ""), ("SmsTo.SenderId", "Alarm")));
        page.ShouldContain("••••••••cdef");
        page.ShouldContain("data-testid=\"smsto-sender\">Alarm");
        page.ShouldContain("Replace the API key");

        page = await ui.SubmitAsync("/settings", "remove-smsto");
        page.ShouldContain("SMS.to settings removed.");
        page.ShouldContain("Not configured");
    }

    [Fact]
    public async Task Invalid_input_shows_field_errors_and_never_echoes_the_key()
    {
        await using var factory = new NotificationApiFactory();
        using var ui = UiClient.For(factory);

        var html = await ui.SubmitInvalidAsync("/settings", "save-smsto", ("SmsTo.ApiKey", ApiKey), ("SmsTo.SenderId", "Not valid!"));

        html.ShouldContain("data-testid=\"error-sender_id\"");
        html.ShouldNotContain(ApiKey);
        html.ShouldContain("value=\"Not valid!\"");
        (await ui.SubmitInvalidAsync("/settings", "save-smsto", ("SmsTo.ApiKey", " "))).ShouldContain("The API key is required.");
        (await ui.GetAsync("/settings")).ShouldContain("Not configured");
    }

    [Fact]
    public async Task AC82_Settings_changes_are_audited_with_the_ha_user_and_never_the_key()
    {
        await using var factory = new NotificationApiFactory();
        using var ui = UiClient.For(factory);
        await ui.SubmitAsync("/settings", "save-smsto", ("SmsTo.ApiKey", ApiKey), ("SmsTo.SenderId", "Home"));
        await ui.SubmitAsync("/settings", "save-smsto", ("SmsTo.ApiKey", "another-key-1234567890"), ("SmsTo.SenderId", "Home"));
        await ui.SubmitAsync("/settings", "remove-smsto");

        var audit = await ui.GetAsync("/audit");

        AuditLogPageTests.AuditRows(audit).ShouldBe(
        [
            "Alice | Delete | Settings sms_to | SMS.to: API key and sender ID removed",
            "Alice | Update | Settings sms_to | SMS.to: API key replaced",
            "Alice | Create | Settings sms_to | SMS.to: API key set, sender ID set to 'Home'",
        ]);
        audit.ShouldNotContain(ApiKey);
        factory.Logs.Entries.ShouldNotContain(e => e.Contains(ApiKey, StringComparison.Ordinal) || e.Contains("another-key-1234567890", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("save-smsto")]
    [InlineData("remove-smsto")]
    public async Task AC83_Settings_forms_reject_posts_without_an_antiforgery_token(string form)
    {
        await using var factory = new NotificationApiFactory();
        using var ui = UiClient.For(factory);
        await ui.SubmitAsync("/settings", "save-smsto", ("SmsTo.ApiKey", ApiKey));

        using var response = await ui.PostAsync("/settings", form, [KeyValuePair.Create("SmsTo.ApiKey", "hijacked-key-000000000")], antiforgery: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ReadSmsToCredentials>().HandleAsync(TestContext.Current.CancellationToken)).Value.ApiKey.ShouldBe(ApiKey);
    }
}
