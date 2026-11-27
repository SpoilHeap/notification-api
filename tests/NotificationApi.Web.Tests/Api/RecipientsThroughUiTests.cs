using System.Net;
using System.Text.Json;
using NotificationApi.Web.Tests.Components;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NotificationApi.Web.Tests.Api;

/// <summary>
/// <c>GET /api/v1/recipients</c> and <c>recipients.changed</c> driven by the web UI (AC32, AC40, AC43, AC45): HA sees
/// handles and names only, the ETag moves exactly when a contact or group is created, renamed or deleted, and the
/// event is sent in the background after the change is saved.
/// </summary>
public sealed class RecipientsThroughUiTests : IAsyncLifetime
{
    private readonly WireMockServer _ha = WireMockServer.Start();
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NotificationApiFactory _factory = default!;
    private UiClient _ui = default!;
    private HttpClient _api = default!;
    private string _token = default!;

    public async ValueTask InitializeAsync()
    {
        // A long webhook timeout, so a request that waited for the webhook inline would visibly hang.
        _factory = new NotificationApiFactory { Settings = { ["Webhook:RequestTimeout"] = "00:01:00" } };
        _ui = UiClient.For(_factory);
        _api = _factory.CreateClient();
        _token = await _factory.GetApiTokenAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _release.TrySetResult();
        _ui.Dispose();
        _api.Dispose();
        await _factory.DisposeAsync();
        _ha.Stop();
        _ha.Dispose();
    }

    [Fact]
    public async Task AC40_Returns_every_contact_and_group_with_no_pii()
    {
        await _ui.CreateGroupAsync("family", "Family");
        await _ui.CreateContactAsync("bob", "Bob", "+353861234567");
        await _ui.CreateContactAsync("alice", "Alice");
        await _ui.SubmitAsync("/contacts/bob", "set-routing", ("route-high", "sms"));
        await _ui.SubmitAsync("/groups/family", "add-member", ("AddMember.Member", "contact:bob"));

        using var response = await GetAsync(ifNoneMatch: null);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(body);
        var recipients = json.RootElement.GetProperty("recipients").EnumerateArray().ToList();
        recipients.Select(r => r.EnumerateObject().Select(p => p.Name).ToArray()).ShouldAllBe(names => names.SequenceEqual(new[] { "handle", "name", "type" }));
        recipients.Select(r => (r.GetProperty("handle").GetString(), r.GetProperty("name").GetString(), r.GetProperty("type").GetString())).ShouldBe(
        [
            ("contact:alice", "Alice", "contact"),
            ("contact:bob", "Bob", "contact"),
            ("group:family", "Family", "group"),
        ]);
        body.ShouldNotContain("353");
        body.ShouldNotContain("sms", Case.Insensitive);
        body.ShouldNotContain("routing", Case.Insensitive);
    }

    [Fact]
    public async Task AC43_The_etag_changes_on_create_rename_and_delete_only()
    {
        var etag = await ETagAsync();

        etag = await ShouldChangeAsync(etag, () => _ui.CreateContactAsync("alice", "Alice"));
        etag = await ShouldChangeAsync(etag, () => _ui.CreateGroupAsync("family", "Family"));
        etag = await ShouldChangeAsync(etag, () => _ui.CreateGroupAsync("kids", "Kids"));

        await ShouldNotChangeAsync(etag, () => _ui.SubmitAsync("/contacts/alice", "set-sms", ("Sms.Number", "+353861234567")));
        await ShouldNotChangeAsync(etag, () => _ui.SubmitAsync("/contacts/alice", "set-routing", ("route-high", "sms")));
        await ShouldNotChangeAsync(etag, () => _ui.SubmitAsync("/groups/family", "add-member", ("AddMember.Member", "contact:alice")));
        await ShouldNotChangeAsync(etag, () => _ui.SubmitAsync("/groups/family", "add-member", ("AddMember.Member", "group:kids")));
        await ShouldNotChangeAsync(etag, () => _ui.SubmitAsync("/groups/family", "remove-member", ("member", "group:kids")));
        await ShouldNotChangeAsync(etag, () => _ui.SubmitAsync("/contacts/alice", "remove-sms"));
        await ShouldNotChangeAsync(etag, () => _ui.SubmitAsync("/contacts/alice", "rename-contact", ("Rename.Name", "Alice"))); // same name
        await ShouldNotChangeAsync(etag, () => _ui.SubmitInvalidAsync("/contacts", "create-contact", ("NewContact.Handle", "alice"), ("NewContact.Name", "Dup")));

        etag = await ShouldChangeAsync(etag, () => _ui.SubmitAsync("/contacts/alice", "rename-contact", ("Rename.Name", "Alice Smith")));
        etag = await ShouldChangeAsync(etag, () => _ui.SubmitAsync("/groups/family", "rename-group", ("Rename.Name", "The Family")));
        etag = await ShouldChangeAsync(etag, () => _ui.SubmitAsync("/contacts/alice/delete", "delete-contact"));
        await ShouldChangeAsync(etag, () => _ui.SubmitAsync("/groups/kids/delete", "delete-group"));

        using var final = await GetAsync(ifNoneMatch: null);
        using var json = JsonDocument.Parse(await final.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("recipients").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ShouldBe(["The Family"]);
    }

    [Fact]
    public async Task AC32_AC45_Recipients_changed_is_sent_in_the_background_with_the_revision_of_the_next_get()
    {
        var received = new TaskCompletionSource<IRequestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ha.Given(Request.Create().UsingPost()).RespondWith(Response.Create().WithCallback(async request =>
        {
            received.TrySetResult(request);
#pragma warning disable VSTHRD003 // The gate is completed by the test, not by work in this context.
            await _release.Task; // HA is slow: it answers only when the test lets it
#pragma warning restore VSTHRD003
            return new ResponseMessage { StatusCode = 200 };
        }));
        (await _api.PutWebhookAsync(_token, _ha.Url + "/api/webhook/abc", TestRequests.NewWebhookSecret())).Dispose();

        var create = _ui.CreateContactAsync("alice", "Alice");
        var first = await Task.WhenAny(create, Task.Delay(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));

        first.ShouldBe(create, "the UI request must not wait for the webhook");
        await create;
        var sent = await received.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        _release.TrySetResult();

        using var json = JsonDocument.Parse(sent.BodyAsBytes!);
        json.RootElement.GetProperty("type").GetString().ShouldBe("recipients.changed");
        var revision = json.RootElement.GetProperty("data").GetProperty("revision").GetString();
        (await ETagAsync()).ShouldBe($"\"{revision}\"");
        sent.Headers!.ShouldContainKey("X-Notification-Api-Signature");
    }

    [Fact]
    public async Task AC32_Every_create_rename_and_delete_sends_one_event_in_order()
    {
        _ha.Given(Request.Create().UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
        (await _api.PutWebhookAsync(_token, _ha.Url + "/api/webhook/abc", TestRequests.NewWebhookSecret())).Dispose();

        await _ui.CreateContactAsync("alice", "Alice", "+353861234567"); // the SMS number sends nothing
        await _ui.CreateGroupAsync("family", "Family", "contact:alice"); // nor does the membership
        await _ui.SubmitAsync("/groups/family", "rename-group", ("Rename.Name", "The Family"));
        await _ui.SubmitAsync("/contacts/alice/delete", "delete-contact");
        await TestRequests.WaitUntilAsync(() => _ha.LogEntries.Count >= 4, "four events are sent");

        var revisions = _ha.LogEntries
            .Select(e => JsonDocument.Parse(e.RequestMessage!.BodyAsBytes!).RootElement)
            .Select(e => (e.GetProperty("type").GetString(), e.GetProperty("data").GetProperty("revision").GetString()))
            .ToList();
        revisions.ShouldBe([("recipients.changed", "2"), ("recipients.changed", "3"), ("recipients.changed", "4"), ("recipients.changed", "5")]);
        (await ETagAsync()).ShouldBe("\"5\"");
    }

    [Fact]
    public async Task AC34_An_unreachable_webhook_does_not_affect_the_change()
    {
        (await _api.PutWebhookAsync(_token, "http://127.0.0.1:1/api/webhook/abc", TestRequests.NewWebhookSecret())).Dispose();

        await _ui.CreateContactAsync("alice", "Alice");
        await TestRequests.WaitUntilAsync(
            () => _factory.Logs.Entries.Any(e => e.StartsWith("Warning", StringComparison.Ordinal) && e.Contains("recipients.changed", StringComparison.Ordinal)),
            "the failed event is logged");

        (await ETagAsync()).ShouldBe("\"2\"");
    }

    private async Task<string> ShouldChangeAsync(string before, Func<Task> change)
    {
        await change();
        var after = await ETagAsync();
        after.ShouldNotBe(before);
        using var old = await GetAsync(before);
        old.StatusCode.ShouldBe(HttpStatusCode.OK);
        return after;
    }

    private async Task ShouldNotChangeAsync(string before, Func<Task> change)
    {
        await change();
        using var response = await GetAsync(before);
        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    private async Task<string> ETagAsync()
    {
        using var response = await GetAsync(ifNoneMatch: null);
        return response.Headers.ETag!.Tag;
    }

    private async Task<HttpResponseMessage> GetAsync(string? ifNoneMatch)
    {
        using var request = TestRequests.Api(HttpMethod.Get, "/api/v1/recipients", _token);
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return await _api.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
