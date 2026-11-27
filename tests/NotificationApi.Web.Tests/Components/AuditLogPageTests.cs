using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Web.Tests.Components;

/// <summary>The read-only Audit log page (AC82), and what may never appear in it or in the logs (AC74).</summary>
public sealed class AuditLogPageTests
{
    private const string Number = "+353861234567";

    [Fact]
    public async Task An_empty_log_says_so()
    {
        await using var factory = new NotificationApiFactory();
        using var ui = UiClient.For(factory);

        (await ui.GetAsync("/audit")).ShouldContain("data-testid=\"empty\"");
    }

    [Fact]
    public async Task AC82_Every_ui_change_is_listed_newest_first_with_the_ha_user()
    {
        await using var factory = new NotificationApiFactory();
        using var ui = UiClient.For(factory);

        await ui.CreateContactAsync("alice", "Alice", Number);
        await ui.SubmitAsync("/contacts/alice", "set-routing", ("route-high", "sms"));
        await ui.SubmitAsync("/contacts/alice", "rename-contact", ("Rename.Name", "Alice Smith"));
        await ui.CreateGroupAsync("family", "Family", "contact:alice");
        await ui.SubmitAsync("/groups/family", "remove-member", ("member", "contact:alice"));
        await ui.SubmitAsync("/groups/family", "rename-group", ("Rename.Name", "The Family"));
        await ui.SubmitAsync("/contacts/alice", "remove-sms");
        await ui.SubmitAsync("/contacts/alice/delete", "delete-contact");
        await ui.SubmitAsync("/groups/family/delete", "delete-group");
        using (var reveal = await ui.PostAsync("/integration", "reveal-api-token"))
        {
            reveal.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var rows = AuditRows(await ui.GetAsync("/audit"));

        rows.ShouldBe(
        [
            "Alice | Reveal | ApiToken home_assistant | API token revealed on the Integration page",
            "Alice | Delete | Group group:family | Deleted group:family (The Family)",
            "Alice | Delete | Contact contact:alice | Deleted contact:alice (Alice Smith)",
            "Alice | Delete | Endpoint contact:alice | SMS endpoint +3538****567 removed (and from routing)",
            "Alice | Update | Group group:family | Renamed from 'Family' to 'The Family'",
            "Alice | Delete | Membership group:family | Removed contact:alice",
            "Alice | Create | Membership group:family | Added contact:alice",
            "Alice | Create | Group group:family | Created group:family (Family)",
            "Alice | Update | Contact contact:alice | Renamed from 'Alice' to 'Alice Smith'",
            "Alice | Update | Routing contact:alice | Routing set to low: none; normal: none; high: SMS; critical: none",
            "Alice | Create | Endpoint contact:alice | SMS number set to +3538****567",
            "Alice | Create | Contact contact:alice | Created contact:alice (Alice)",
        ]);
        (await ui.GetAsync("/audit")).ShouldContain($"title=\"HA user id {NotificationApiFactory.HaUserId}\"");
    }

    [Fact]
    public async Task AC82_Send_test_event_is_listed()
    {
        await using var factory = new NotificationApiFactory();
        using var ui = UiClient.For(factory);
        (await factory.CreateClient().PutWebhookAsync(await factory.GetApiTokenAsync(), "http://127.0.0.1:1/api/webhook/abc", TestRequests.NewWebhookSecret())).Dispose();

        using (var sent = await ui.PostAsync("/integration", "send-test-event"))
        {
            sent.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        AuditRows(await ui.GetAsync("/audit")).ShouldHaveSingleItem()
            .ShouldMatch(@"^Alice \| Send \| WebhookEvent evt_[0-9a-f]{32} \| Sent a webhook.test event: failed \(no response\)$");
    }

    [Fact]
    public async Task The_log_is_paged()
    {
        await using var factory = new NotificationApiFactory();
        using var ui = UiClient.For(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var trail = scope.ServiceProvider.GetRequiredService<AuditTrail>();
            for (var i = 0; i < GetAuditLog.PageSize + 1; i++)
            {
                trail.Record(new AuditActor("u", "U"), AuditAction.Update, AuditEntityType.Contact, "contact:x", $"change {i}");
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var first = await ui.GetAsync("/audit");
        var second = await ui.GetAsync("/audit?page=2");

        first.ShouldContain($"Page 1 of 2 ({GetAuditLog.PageSize + 1} entries)");
        first.ShouldContain("<a href=\"audit?page=2\" data-testid=\"older\">");
        AuditRows(first)[0].ShouldEndWith($"change {GetAuditLog.PageSize}");
        AuditRows(second).ShouldHaveSingleItem().ShouldEndWith("change 0");
        second.ShouldContain("<a href=\"audit?page=1\" data-testid=\"newer\">");
    }

    [Fact]
    public async Task AC74_No_full_phone_number_reaches_the_logs_the_audit_log_or_any_page()
    {
        await using var factory = new NotificationApiFactory();
        // Log everything, including framework Debug/Trace output, so nothing can hide below the default level.
        await using var verbose = factory.WithWebHostBuilder(b => b.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace)));
        using var ui = UiClient.For(verbose);
        var pages = new List<string>
        {
            await ui.CreateContactAsync("alice", "Alice", "+353 86 123 4567"),
            await ui.SubmitAsync("/contacts/alice", "set-sms", ("Sms.Number", "+353 87 765 4321")),
            await ui.SubmitAsync("/contacts/alice", "set-routing", ("route-normal", "sms")),
            await ui.GetAsync("/contacts"),
        };
        // A rejected number is only echoed back in its own input, for correcting; it's never logged.
        (await ui.SubmitInvalidAsync("/contacts/alice", "set-sms", ("Sms.Number", "0861234567"))).ShouldContain("data-testid=\"error-phone_number\"");
        pages.Add(await ui.SubmitAsync("/contacts/alice", "remove-sms"));
        pages.Add(await ui.GetAsync("/audit"));

        string[] secrets = ["353861234567", "861234567", "353877654321", "877654321", "86 123 4567", "87 765 4321"];
        factory.Logs.Entries.ShouldNotBeEmpty();
        foreach (var secret in secrets.Append("0861234567"))
        {
            factory.Logs.Entries.ShouldNotContain(e => e.Contains(secret, StringComparison.Ordinal), secret);
        }

        foreach (var secret in secrets)
        {
            pages.ShouldNotContain(p => WebUtility.HtmlDecode(p).Contains(secret, StringComparison.Ordinal), secret);
        }

        var audit = WebUtility.HtmlDecode(pages[^1]);
        audit.ShouldContain("+3538****567");
        audit.ShouldContain("+3538****321");
        factory.Logs.Entries.ShouldContain(e => e.Contains("+3538****321", StringComparison.Ordinal));
    }

    /// <summary>Each row as "user | action | what | details", with the markup stripped.</summary>
    internal static List<string> AuditRows(string html)
    {
        var body = html[html.IndexOf("<tbody>", StringComparison.Ordinal)..html.IndexOf("</tbody>", StringComparison.Ordinal)];
        return System.Text.RegularExpressions.Regex.Matches(body, "<tr>(.*?)</tr>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(row => System.Text.RegularExpressions.Regex.Matches(row.Groups[1].Value, "<td[^>]*>(.*?)</td>", System.Text.RegularExpressions.RegexOptions.Singleline)
                .Select(cell => WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(cell.Groups[1].Value, "<[^>]+>", string.Empty)).Trim())
                .Skip(1) // the time
                .ToArray())
            .Select(cells => string.Join(" | ", cells))
            .ToList();
    }
}
