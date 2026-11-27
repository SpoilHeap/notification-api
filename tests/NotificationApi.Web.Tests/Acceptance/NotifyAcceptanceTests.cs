using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationApi.Domain.Notifications;
using NotificationApi.Infrastructure.Persistence;

namespace NotificationApi.Web.Tests.Acceptance;

/// <summary>Spec §12.1 through <c>POST /api/v1/notify</c>, with recording fake channels (AC01–AC10, AC12).</summary>
public sealed class NotifyAcceptanceTests : IAsyncLifetime
{
    private DeliveryApp _app = default!;

    public async ValueTask InitializeAsync()
    {
        _app = await DeliveryApp.StartAsync();
        await _app.SeedFixturesAsync();
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task AC01_Normal_priority_to_alice_is_one_telegram_delivery_and_one_message()
    {
        var accepted = await _app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, title = "Front door", message = "Door opened", priority = "normal" });

        accepted.NotificationId.ShouldMatch("^ntf_[0-9a-f]{32}$");
        var delivery = accepted.Deliveries.ShouldHaveSingleItem();
        delivery.DeliveryId.ShouldMatch("^dlv_[0-9a-f]{32}$");
        (delivery.Contact, delivery.Via, delivery.Channel, delivery.Status).ShouldBe(("alice", "contact:alice", "telegram", "queued"));
        await _app.WaitForFinalAsync(accepted.NotificationId);
        _app.Telegram.Sent.ShouldHaveSingleItem().Address.ShouldBe(DeliveryApp.AliceChat);
        _app.Sms.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC01_The_202_body_has_exactly_the_spec_fields()
    {
        using var response = await _app.NotifyAsync(new { to = new[] { "contact:alice" }, message = "m", priority = "normal" });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["notification_id", "deliveries"]);
        json.RootElement.GetProperty("deliveries")[0].EnumerateObject().Select(p => p.Name).ShouldBe(["delivery_id", "contact", "via", "channel", "status"]);
    }

    [Fact]
    public async Task AC02_High_priority_creates_telegram_and_sms_deliveries_and_both_channels_send()
    {
        var accepted = await _app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, message = "Alarm", priority = "high" });

        accepted.Routes.ShouldBe(["alice/contact:alice/telegram", "alice/contact:alice/sms"]);
        var deliveries = await _app.WaitForFinalAsync(accepted.NotificationId);
        deliveries.ShouldAllBe(d => d.Status == DeliveryStatus.Sent);
        _app.Telegram.Sent.ShouldHaveSingleItem().Address.ShouldBe(DeliveryApp.AliceChat);
        _app.Sms.Sent.ShouldHaveSingleItem().Address.ShouldBe(DeliveryApp.AliceNumber);
    }

    [Fact]
    public async Task AC03_Contact_in_two_groups_receives_two_messages_per_routed_channel()
    {
        await _app.GroupAsync("family", "contact:alice");
        await _app.GroupAsync("security", "contact:alice");

        var accepted = await _app.NotifyAcceptedAsync(new { to = new[] { "group:family", "group:security" }, message = "m", priority = "high" });

        accepted.Routes.ShouldBe(["alice/group:family/telegram", "alice/group:family/sms", "alice/group:security/telegram", "alice/group:security/sms"]);
        await _app.WaitForFinalAsync(accepted.NotificationId);
        _app.Telegram.Sent.Count.ShouldBe(2);
        _app.Sms.Sent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AC04_A_group_and_the_contact_itself_give_two_messages()
    {
        await _app.GroupAsync("family", "contact:alice");

        var accepted = await _app.NotifyAcceptedAsync(new { to = new[] { "group:family", "contact:alice" }, message = "m", priority = "normal" });

        accepted.Routes.ShouldBe(["alice/group:family/telegram", "alice/contact:alice/telegram"]);
        await _app.WaitForFinalAsync(accepted.NotificationId);
        _app.Telegram.Sent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AC05_Nested_groups_reach_alice_twice_both_via_the_top_level_group()
    {
        await _app.GroupAsync("kids", "contact:alice");
        await _app.GroupAsync("family", "group:kids", "contact:alice");

        var accepted = await _app.NotifyAcceptedAsync(new { to = new[] { "group:family" }, message = "m", priority = "normal" });

        accepted.Routes.ShouldBe(["alice/group:family/telegram", "alice/group:family/telegram"]);
        await _app.WaitForFinalAsync(accepted.NotificationId);
        _app.Telegram.Sent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AC06_An_sms_override_reaches_alice_only_and_bob_gets_nothing()
    {
        var accepted = await _app.NotifyAcceptedAsync(new { to = new[] { "contact:alice", "contact:bob" }, message = "m", priority = "normal", channels = new[] { "sms" } });

        accepted.Routes.ShouldBe(["alice/contact:alice/sms"]);
        await _app.WaitForFinalAsync(accepted.NotificationId);
        _app.Sms.Sent.ShouldHaveSingleItem();
        _app.Telegram.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC07_A_routed_channel_without_a_verified_endpoint_is_skipped()
    {
        await _app.InScopeAsync(sp => sp.GetRequiredService<NotificationDbContext>().Database.ExecuteSqlRawAsync(
            "UPDATE ContactEndpoints SET VerifiedAt = NULL WHERE Channel = 'Sms'", TestContext.Current.CancellationToken));

        var accepted = await _app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, message = "m", priority = "high" });

        accepted.Routes.ShouldBe(["alice/contact:alice/telegram"]);
    }

    [Theory]
    [InlineData("contact:carol", "None of the 1 resolved contact(s) has a verified endpoint for the selected channels.")]
    [InlineData("group:empty", "The recipients resolved to no contacts (for example, only empty groups).")]
    public async Task AC08_Zero_deliveries_is_422_no_deliveries_and_nothing_is_queued(string handle, string detail)
    {
        await _app.GroupAsync("empty");

        using var response = await _app.NotifyAsync(new { to = new[] { handle }, message = "m", priority = "high", idempotency_key = "k" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(422);
        json.RootElement.GetProperty("title").GetString().ShouldBe("No deliveries");
        json.RootElement.GetProperty("detail").GetString().ShouldBe(detail);
        json.RootElement.TryGetProperty("errors", out _).ShouldBeFalse();
        (await _app.NotificationCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AC09_Unknown_handles_are_400_naming_each_and_nothing_is_queued_for_anyone()
    {
        using var response = await _app.NotifyAsync(new { to = new[] { "contact:alice", "contact:zed", "group:nope" }, message = "m", priority = "normal" });

        var errors = await ValidationErrorsAsync(response);
        errors.Keys.ShouldBe(["to"]);
        errors["to"].ShouldBe(["Unknown handle: contact:zed", "Unknown handle: group:nope"]);
        (await _app.NotificationCountAsync()).ShouldBe(0);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        _app.Telegram.Sent.ShouldBeEmpty();
    }

    public static TheoryData<string, string> InvalidPayloads => new()
    {
        { """{"to":["contact:alice"],"priority":"normal"}""", "message" },
        { """{"to":["contact:alice"],"message":"","priority":"normal"}""", "message" },
        { """{"to":[],"message":"m","priority":"normal"}""", "to" },
        { """{"message":"m","priority":"normal"}""", "to" },
        { """{"to":["alice"],"message":"m","priority":"normal"}""", "to" },
        { """{"to":["contact:alice"],"message":"m","priority":"urgent"}""", "priority" },
        { """{"to":["contact:alice"],"message":"m","priority":"High"}""", "priority" },
        { """{"to":["contact:alice"],"message":"m"}""", "priority" },
        { """{"to":["contact:alice"],"message":"m","priority":"normal","channels":["email"]}""", "channels" },
        { """{"to":["contact:alice"],"message":"m","priority":"normal","channels":["SMS"]}""", "channels" },
        { """{"to":["contact:alice"],"message":"m","priority":"normal","actions":[{"title":"Open"}]}""", "actions" },
        { """{"to":["contact:alice"],"message":"m","priority":"normal","actions":"open"}""", "actions" },
        { $$"""{"to":["contact:alice"],"message":"m","priority":"normal","title":"{{new string('t', 201)}}"}""", "title" },
        { $$"""{"to":["contact:alice"],"message":"{{new string('m', 4001)}}","priority":"normal"}""", "message" },
        { $$"""{"to":["contact:alice"],"message":"m","priority":"normal","tag":"{{new string('t', 201)}}"}""", "tag" },
        { $$"""{"to":["contact:alice"],"message":"m","priority":"normal","source":"{{new string('s', 201)}}"}""", "source" },
        { $$"""{"to":["contact:alice"],"message":"m","priority":"normal","idempotency_key":"{{new string('k', 201)}}"}""", "idempotency_key" },
    };

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task AC10_An_invalid_payload_is_400_problem_details_with_a_field_error(string json, string field)
    {
        using var response = await _app.NotifyRawAsync(json);

        (await ValidationErrorsAsync(response)).Keys.ShouldBe([field]);
        (await _app.NotificationCountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("""{"to":["contact:alice"],"message":"m","priority":"normal","title":null,"tag":null,"source":null,"idempotency_key":null,"channels":null,"actions":null}""")]
    [InlineData("""{"to":["contact:alice"],"message":"m","priority":"normal","channels":[],"actions":[]}""")]
    [InlineData("""{"to":["contact:alice"],"message":"m","priority":"normal"}""")]
    public async Task Null_means_omitted_and_empty_channels_or_actions_are_accepted(string json)
    {
        using var response = await _app.NotifyRawAsync(json);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("deliveries")[0].GetProperty("channel").GetString().ShouldBe("telegram"); // routing decided
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"to":"contact:alice","message":"m","priority":"normal"}""")]
    public async Task A_malformed_body_is_400(string json)
    {
        using var response = await _app.NotifyRawAsync(json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _app.NotificationCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AC12_The_same_key_returns_the_original_response_and_sends_nothing_new()
    {
        var first = await _app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, message = "m", priority = "high", idempotency_key = "K-1" });
        await _app.WaitForFinalAsync(first.NotificationId);

        var again = await _app.NotifyAcceptedAsync(new { to = new[] { "contact:bob" }, message = "other", priority = "low", idempotency_key = "K-1" });

        again.Text.ShouldBe(first.Text);
        again.Deliveries.ShouldAllBe(d => d.Status == "queued");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        (_app.Telegram.Calls + _app.Sms.Calls).ShouldBe(2);
        (await _app.NotificationCountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task AC12_A_rejected_request_does_not_record_its_key()
    {
        using (var rejected = await _app.NotifyAsync(new { to = new[] { "contact:zed" }, message = "m", priority = "normal", idempotency_key = "K-2" }))
        {
            rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        var accepted = await _app.NotifyAcceptedAsync(new { to = new[] { "contact:alice" }, message = "m", priority = "normal", idempotency_key = "K-2" });

        accepted.Deliveries.ShouldHaveSingleItem();
    }

    internal static async Task<Dictionary<string, string[]>> ValidationErrorsAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("errors").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(e => e.GetString()!).ToArray());
    }
}

/// <summary>AC12's 24 h window, on fake time.</summary>
public sealed class IdempotencyWindowTests
{
    [Fact]
    public async Task AC12_After_24_hours_the_key_is_treated_as_new()
    {
        await using var app = await DeliveryApp.StartAsync(fakeTime: true);
        await app.SeedFixturesAsync();
        var body = new { to = new[] { "contact:alice" }, message = "m", priority = "normal", idempotency_key = "K" };
        var first = await app.NotifyAcceptedAsync(body);
        await app.WaitForFinalAsync(first.NotificationId);

        app.Time!.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        (await app.NotifyAcceptedAsync(body)).Text.ShouldBe(first.Text);
        app.Time.Advance(TimeSpan.FromSeconds(2));
        var later = await app.NotifyAcceptedAsync(body);

        later.NotificationId.ShouldNotBe(first.NotificationId);
        await app.WaitForFinalAsync(later.NotificationId);
        app.Telegram.Calls.ShouldBe(2);
    }
}
