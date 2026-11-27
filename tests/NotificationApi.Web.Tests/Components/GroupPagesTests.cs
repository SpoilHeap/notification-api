using System.Net;

namespace NotificationApi.Web.Tests.Components;

/// <summary>The Groups pages (spec §12.5).</summary>
public sealed class GroupPagesTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task AC54_Every_group_page_renders_through_ingress()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateGroupAsync("render", "Render");

        foreach (var path in new[] { "/groups", "/groups/render", "/groups/render/delete" })
        {
            (await ui.GetAsync(path)).ShouldContain("<nav>");
        }
    }

    [Fact]
    public async Task AC54_A_group_holds_contacts_and_other_groups()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync("member_a", "Member A");
        await ui.CreateGroupAsync("inner", "Inner");

        var page = await ui.CreateGroupAsync("outer", "Outer", "contact:member_a", "group:inner");

        page.ShouldContain("Member added.");
        page.ShouldContain("<a href=\"contacts/member_a\"><code>contact:member_a</code></a>");
        page.ShouldContain("<a href=\"groups/inner\"><code>group:inner</code></a>");
        page.ShouldContain("<input value=\"group:outer\" readonly data-testid=\"handle\"");
        page.ShouldNotContain("<option value=\"contact:member_a\""); // members aren't offered again
        (await ui.GetAsync("/groups")).ShouldContain("<td>1 contact, 1 group</td>");
    }

    [Fact]
    public async Task AC55_Self_and_cyclic_membership_is_rejected_with_a_clear_message()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateGroupAsync("c_teens", "Teens");
        await ui.CreateGroupAsync("c_kids", "Kids", "group:c_teens");
        await ui.CreateGroupAsync("c_family", "Family", "group:c_kids");

        var self = await ui.SubmitInvalidAsync("/groups/c_family", "add-member", ("AddMember.Member", "group:c_family"));
        var direct = await ui.SubmitInvalidAsync("/groups/c_kids", "add-member", ("AddMember.Member", "group:c_family"));
        var indirect = await ui.SubmitInvalidAsync("/groups/c_teens", "add-member", ("AddMember.Member", "group:c_family"));

        self.ShouldContain("A group can&#x27;t be a member of itself.");
        direct.ShouldContain("group:c_family already contains group:c_kids (group:c_family &#x2192; group:c_kids), so adding it would create a loop.");
        indirect.ShouldContain("group:c_family already contains group:c_teens (group:c_family &#x2192; group:c_kids &#x2192; group:c_teens), so adding it would create a loop.");
        indirect.ShouldContain("data-testid=\"error-member\"");
        indirect.ShouldContain("<option value=\"group:c_family\" selected>"); // the choice is kept
        (await ui.GetAsync("/groups/c_teens")).ShouldContain("data-testid=\"no-members\"");
    }

    [Fact]
    public async Task Choosing_nothing_shows_a_field_error()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateGroupAsync("picky", "Picky");

        (await ui.SubmitInvalidAsync("/groups/picky", "add-member", ("AddMember.Member", ""))).ShouldContain("Choose a contact or group to add.");
    }

    [Fact]
    public async Task Removing_a_member_keeps_it()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync("kept", "Kept");
        await ui.CreateGroupAsync("trimmed", "Trimmed", "contact:kept");

        var page = await ui.SubmitAsync("/groups/trimmed", "remove-member", ("member", "contact:kept"));

        page.ShouldContain("Member removed.");
        page.ShouldContain("data-testid=\"no-members\"");
        await ui.GetAsync("/contacts/kept");
    }

    [Fact]
    public async Task Invalid_group_input_shows_field_errors()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateGroupAsync("dupe", "Dupe");

        var html = await ui.SubmitInvalidAsync("/groups", "create-group", ("NewGroup.Handle", "dupe"), ("NewGroup.Name", ""));
        var rename = await ui.SubmitInvalidAsync("/groups/dupe", "rename-group", ("Rename.Name", " "));

        html.ShouldContain("group:dupe already exists");
        html.ShouldContain("data-testid=\"error-name\"");
        rename.ShouldContain("data-testid=\"error-name\"");
    }

    [Fact]
    public async Task AC51_Renaming_a_group_keeps_its_handle()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateGroupAsync("named", "Before");

        var page = await ui.SubmitAsync("/groups/named", "rename-group", ("Rename.Name", "After"));

        page.ShouldContain("<h1>After</h1>");
        page.ShouldContain("value=\"group:named\" readonly");
    }

    [Fact]
    public async Task AC56_Deleting_a_group_lists_its_parents_removes_it_from_them_and_keeps_its_members()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateContactAsync("survivor", "Survivor");
        await ui.CreateGroupAsync("doomed", "Doomed", "contact:survivor");
        await ui.CreateGroupAsync("parent_one", "Parent One", "group:doomed");

        (await ui.GetAsync("/groups/doomed/delete"))
            .ShouldContain("<ul data-testid=\"member-of\"><li><code>group:parent_one</code> (Parent One)</li></ul>");
        var list = await ui.SubmitAsync("/groups/doomed/delete", "delete-group");

        list.ShouldContain("Deleted group:doomed.");
        (await ui.GetAsync("/groups/parent_one")).ShouldContain("data-testid=\"no-members\"");
        await ui.GetAsync("/contacts/survivor");
        await ui.GetAsync("/groups/doomed", HttpStatusCode.NotFound);
        (await ui.GetAsync("/groups/parent_one/delete")).ShouldContain("data-testid=\"member-of-none\"");
    }

    [Theory]
    [InlineData("/groups/nobody")]
    [InlineData("/groups/nobody/delete")]
    public async Task An_unknown_group_is_404(string path)
    {
        using var ui = UiClient.For(factory);

        (await ui.GetAsync(path, HttpStatusCode.NotFound)).ShouldContain("data-testid=\"not-found\"");
    }

    [Fact]
    public async Task Posting_a_stale_form_for_a_deleted_group_is_rejected()
    {
        using var ui = UiClient.For(factory);
        await ui.CreateGroupAsync("stale", "Stale");
        var rename = IntegrationPageTests.ParseForm(await ui.GetAsync("/groups/stale"), "rename-group");
        var delete = IntegrationPageTests.ParseForm(await ui.GetAsync("/groups/stale/delete"), "delete-group");
        await ui.SubmitAsync("/groups/stale/delete", "delete-group");

        using var renamed = await ui.PostFieldsAsync("/groups/stale", rename.Append(KeyValuePair.Create("Rename.Name", "Boo")));
        using var deleted = await ui.PostFieldsAsync("/groups/stale/delete", delete);

        // The forms aren't on the page any more, so Blazor has no handler for them: rejected, and nothing changes.
        renamed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        deleted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public static TheoryData<string, string, string, string> GroupForms => new()
    {
        { "/groups", "create-group", "NewGroup.Handle", "csrf_new" },
        { "/groups/csrf", "rename-group", "Rename.Name", "Hacked" },
        { "/groups/csrf", "add-member", "AddMember.Member", "contact:csrf_member" },
        { "/groups/csrf", "remove-member", "member", "contact:csrf_member" },
        { "/groups/csrf/delete", "delete-group", "x", "y" },
    };

    [Theory]
    [MemberData(nameof(GroupForms))]
    public async Task AC83_Group_forms_reject_posts_without_an_antiforgery_token(string path, string form, string field, string value)
    {
        using var ui = UiClient.For(factory);
        await EnsureCsrfGroupAsync(ui);

        using var response = await ui.PostAsync(path, form, [KeyValuePair.Create(field, value), KeyValuePair.Create("NewGroup.Name", "x")], antiforgery: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var page = await ui.GetAsync("/groups/csrf");
        page.ShouldContain("<h1>CSRF</h1>");
        page.ShouldContain("<code>contact:csrf_member</code>");
        await ui.GetAsync("/groups/csrf_new", HttpStatusCode.NotFound);
    }

    private async Task EnsureCsrfGroupAsync(UiClient ui)
    {
        using var probe = factory.CreateClient();
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, "/groups/csrf");
        using var response = await probe.SendAsync(request, TestContext.Current.CancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await ui.CreateContactAsync("csrf_member", "CSRF member");
            await ui.CreateGroupAsync("csrf", "CSRF", "contact:csrf_member");
        }
    }
}
