using System.Net;

namespace NotificationApi.Web.Tests.Components;

/// <summary>The Contacts pages (spec §12.5): Blazor SSR forms with antiforgery and Post/Redirect/Get.</summary>
public sealed class ContactPagesTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    private const string Number = "+353861234567";
    private const string Masked = "+3538****567";

    [Fact]
    public async Task AC50_Every_contact_page_renders_through_ingress()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync("render", "Render Me");

        foreach (var path in new[] { "/contacts", "/contacts/render", "/contacts/render/delete" })
        {
            var html = await ui.GetAsync(path);
            html.ShouldContain("<nav>");
            html.ShouldContain("href=\"contacts\"");
            html.ShouldContain("href=\"groups\"");
            html.ShouldContain("href=\"audit\"");
        }
    }

    [Fact]
    public async Task AC50_Creating_a_contact_redirects_to_its_page()
    {
        using var ui = UiClient.For(factory);

        using var response = await ui.PostAsync("/contacts", "create-contact", Fields(("NewContact.Handle", "created"), ("NewContact.Name", "  Created Person ")));

        response.StatusCode.ShouldBe(HttpStatusCode.SeeOther);
        response.Headers.Location!.OriginalString.ShouldBe("/contacts/created?saved=created");
        var page = await ui.GetAsync("/contacts/created?saved=created");
        page.ShouldContain("<h1>Created Person</h1>");
        page.ShouldContain("data-testid=\"flash\"");
        (await ui.GetAsync("/contacts")).ShouldContain("<code>contact:created</code>");
    }

    [Fact]
    public async Task AC50_Invalid_input_shows_field_errors_without_redirecting()
    {
        using var ui = UiClient.For(factory);

        var html = await ui.SubmitInvalidAsync("/contacts", "create-contact", ("NewContact.Handle", "Not Valid"), ("NewContact.Name", " "));

        html.ShouldContain("data-testid=\"error-handle\"");
        html.ShouldContain("lower-case letters, digits or underscores");
        html.ShouldContain("data-testid=\"error-name\"");
        html.ShouldContain("The name is required.");
        html.ShouldContain("value=\"Not Valid\""); // the typed value is kept
    }

    [Fact]
    public async Task AC57_A_duplicate_contact_handle_is_rejected_but_a_group_may_share_it()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync("shared", "Shared");
        await ui.CreateGroupAsync("shared", "Shared group");

        var html = await ui.SubmitInvalidAsync("/contacts", "create-contact", ("NewContact.Handle", "shared"), ("NewContact.Name", "Again"));

        html.ShouldContain("contact:shared already exists");
    }

    [Fact]
    public async Task AC51_The_handle_is_read_only_and_renaming_keeps_it()
    {
        using var ui = UiClient.For(factory);
        var page = await ui.CreateContactAsync("renamed", "Before");
        page.ShouldContain("<input value=\"contact:renamed\" readonly data-testid=\"handle\"");
        IntegrationPageTests.ParseForm(page, "rename-contact").Keys.ShouldNotContain(k => k.Contains("Handle", StringComparison.Ordinal));

        page = await ui.SubmitAsync("/contacts/renamed", "rename-contact", ("Rename.Name", "After"), ("NewContact.Handle", "hijack"));

        page.ShouldContain("<h1>After</h1>");
        page.ShouldContain("Name saved.");
        page.ShouldContain("value=\"contact:renamed\" readonly");
        await ui.GetAsync("/contacts/hijack", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Renaming_to_an_empty_name_shows_a_field_error()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync("blank", "Blank");

        var html = await ui.SubmitInvalidAsync("/contacts/blank", "rename-contact", ("Rename.Name", ""));

        html.ShouldContain("data-testid=\"error-name\"");
    }

    [Fact]
    public async Task AC52_An_sms_number_is_stored_in_E164_and_only_ever_shown_masked()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync("texter", "Texter");

        var page = WebUtility.HtmlDecode(await ui.SubmitAsync("/contacts/texter", "set-sms", ("Sms.Number", "+353 (86) 123-4567")));

        page.ShouldContain("SMS number saved.");
        page.ShouldContain($"<code data-testid=\"sms-number\">{Masked}</code>");
        page.ShouldNotContain(Number);
        page.ShouldNotContain("+353 (86)");
        WebUtility.HtmlDecode(await ui.GetAsync("/contacts")).ShouldContain($"SMS {Masked}");
    }

    [Theory]
    [InlineData("0861234567")]
    [InlineData("+35386")]
    [InlineData("")]
    public async Task AC52_An_invalid_sms_number_shows_a_field_error(string number)
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync($"bad_sms_{number.Length}", "Bad SMS");

        var html = await ui.SubmitInvalidAsync($"/contacts/bad_sms_{number.Length}", "set-sms", ("Sms.Number", number));

        html.ShouldContain("data-testid=\"error-phone_number\"");
    }

    [Fact]
    public async Task AC53_Routing_offers_only_channels_with_endpoints_and_telegram_is_not_linked_yet()
    {
        using var ui = UiClient.For(factory);
        var page = await ui.CreateContactAsync("router", "Router");

        page.ShouldContain("Not linked — coming soon");
        page.ShouldContain("name=\"route-high\" value=\"sms\" aria-label=\"high via SMS\" disabled");
        page.ShouldContain("name=\"route-high\" value=\"telegram\" aria-label=\"high via Telegram\" disabled");

        page = await ui.SubmitAsync("/contacts/router", "set-sms", ("Sms.Number", Number));
        page.ShouldContain("name=\"route-high\" value=\"sms\" aria-label=\"high via SMS\" />");
        page.ShouldContain("name=\"route-high\" value=\"telegram\" aria-label=\"high via Telegram\" disabled");

        page = await ui.SubmitAsync("/contacts/router", "set-routing", ("route-high", "sms"), ("route-critical", "sms"));
        page.ShouldContain("Routing saved.");
        page.ShouldContain("aria-label=\"high via SMS\" checked");
        page.ShouldContain("aria-label=\"critical via SMS\" checked");
        page.ShouldContain("aria-label=\"normal via SMS\" />");
    }

    [Theory]
    [InlineData("telegram", "high: Telegram isn&#x27;t set up for this contact")]
    [InlineData("pigeon", "Unknown channel.")]
    public async Task AC53_Routing_to_an_unavailable_channel_shows_a_field_error(string channel, string message)
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync($"route_{channel}", "Router", Number);

        var html = await ui.SubmitInvalidAsync($"/contacts/route_{channel}", "set-routing", ("route-high", channel));

        html.ShouldContain("data-testid=\"error-routing\"");
        html.ShouldContain(message);
    }

    [Fact]
    public async Task Removing_the_sms_number_also_removes_it_from_routing()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync("unsms", "Un SMS", Number);
        await ui.SubmitAsync("/contacts/unsms", "set-routing", ("route-high", "sms"));

        var page = await ui.SubmitAsync("/contacts/unsms", "remove-sms");

        page.ShouldContain("SMS number removed.");
        page.ShouldNotContain("data-testid=\"sms-number\"");
        page.ShouldContain("aria-label=\"high via SMS\" disabled");
    }

    [Fact]
    public async Task AC56_Deleting_a_contact_lists_its_groups_then_removes_the_memberships()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync("leaver", "Leaver");
        await ui.CreateGroupAsync("leaver_a", "Group A", "contact:leaver");
        await ui.CreateGroupAsync("leaver_b", "Group B", "contact:leaver");

        var confirm = await ui.GetAsync("/contacts/leaver/delete");
        confirm.ShouldContain("<ul data-testid=\"member-of\"><li><code>group:leaver_a</code> (Group A)</li><li><code>group:leaver_b</code> (Group B)</li></ul>");

        var list = await ui.SubmitAsync("/contacts/leaver/delete", "delete-contact");

        list.ShouldContain("Deleted contact:leaver.");
        list.ShouldNotContain("contact:leaver<");
        (await ui.GetAsync("/groups/leaver_a")).ShouldContain("data-testid=\"no-members\"");
        await ui.GetAsync("/contacts/leaver", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_contact_in_no_group_is_deleted_after_a_plain_confirmation()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync("loner", "Loner");

        (await ui.GetAsync("/contacts/loner/delete")).ShouldContain("data-testid=\"member-of-none\"");
        (await ui.SubmitAsync("/contacts/loner/delete", "delete-contact")).ShouldContain("Deleted contact:loner.");
    }

    [Theory]
    [InlineData("/contacts/nobody")]
    [InlineData("/contacts/nobody/delete")]
    [InlineData("/contacts/NOT_VALID")]
    public async Task An_unknown_contact_is_404(string path)
    {
        using var ui = UiClient.For(factory);

        (await ui.GetAsync(path, HttpStatusCode.NotFound)).ShouldContain("data-testid=\"not-found\"");
    }

    [Fact]
    public async Task Posting_a_stale_form_for_a_deleted_contact_is_rejected()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync("ghost", "Ghost");
        var stale = IntegrationPageTests.ParseForm(await ui.GetAsync("/contacts/ghost"), "rename-contact");
        await ui.SubmitAsync("/contacts/ghost/delete", "delete-contact");

        using var response = await ui.PostFieldsAsync("/contacts/ghost", stale.Append(KeyValuePair.Create("Rename.Name", "Boo")));

        // The form isn't on the page any more, so Blazor has no handler for it: rejected, and nothing changes.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ui.GetAsync("/contacts/ghost", HttpStatusCode.NotFound);
    }

    public static TheoryData<string, string, string, string> ContactForms => new()
    {
        { "/contacts", "create-contact", "NewContact.Handle", "csrf_new" },
        { "/contacts/csrf", "rename-contact", "Rename.Name", "Hacked" },
        { "/contacts/csrf", "set-sms", "Sms.Number", Number },
        { "/contacts/csrf", "set-routing", "route-high", "sms" },
        { "/contacts/csrf", "remove-sms", "x", "y" },
        { "/contacts/csrf/delete", "delete-contact", "x", "y" },
    };

    [Theory]
    [MemberData(nameof(ContactForms))]
    public async Task AC83_Contact_forms_reject_posts_without_an_antiforgery_token(string path, string form, string field, string value)
    {
        using var ui = UiClient.For(factory);
        await EnsureCsrfContactAsync(ui);

        using var response = await ui.PostAsync(path, form, Fields((field, value), ("NewContact.Name", "x")), antiforgery: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var page = WebUtility.HtmlDecode(await ui.GetAsync("/contacts/csrf"));
        page.ShouldContain("<h1>CSRF</h1>");
        page.ShouldContain($"{Masked}</code>");
        page.ShouldContain("aria-label=\"high via SMS\" />");
        await ui.GetAsync("/contacts/csrf_new", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Forms_and_redirects_stay_under_the_ingress_prefix()
    {
        const string Prefix = "/api/hassio_ingress/tok123";
        using (var ui = UiClient.For(factory))
        {
            await ui.CreateContactAsync("prefixed", "Prefixed");
        }

        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        using var get = NotificationApiFactory.IngressRequest(HttpMethod.Get, "/contacts/prefixed");
        get.Headers.Add("X-Ingress-Path", Prefix);
        using var page = await client.SendAsync(get, TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.ShouldContain($"<base href=\"{Prefix}/\" />");
        html.ShouldContain($"action=\"{Prefix}/contacts/prefixed\"");
        var cookie = page.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal)).Split(';')[0];

        using var post = NotificationApiFactory.IngressRequest(HttpMethod.Post, "/contacts/prefixed");
        post.Headers.Add("X-Ingress-Path", Prefix);
        post.Headers.Add("Cookie", cookie);
        post.Content = new FormUrlEncodedContent(IntegrationPageTests.ParseForm(html, "rename-contact").Append(KeyValuePair.Create("Rename.Name", "Prefixed")));
        using var response = await client.SendAsync(post, TestContext.Current.CancellationToken);

        // Path-absolute, no scheme or host: the browser stays on Home Assistant's origin.
        response.StatusCode.ShouldBe(HttpStatusCode.SeeOther);
        response.Headers.Location!.OriginalString.ShouldBe($"{Prefix}/contacts/prefixed?saved=name");
    }

    private async Task EnsureCsrfContactAsync(UiClient ui)
    {
        using var probe = factory.CreateClient();
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, "/contacts/csrf");
        using var response = await probe.SendAsync(request, TestContext.Current.CancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await ui.CreateContactAsync("csrf", "CSRF", Number);
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> Fields(params (string Name, string Value)[] fields) =>
        fields.Select(f => KeyValuePair.Create(f.Name, f.Value));
}
