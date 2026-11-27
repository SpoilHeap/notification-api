using NotificationApi.Application.Notifications;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Tests.Notifications;

/// <summary>The request rules of spec §4.1 (v1.4), AC10.</summary>
public sealed class SendNotificationValidatorTests
{
    private static readonly SendNotificationCommand Valid = new(["contact:alice", "group:family"], "Title", "Message", "high", ["telegram", "sms"], "tag", "automation.x", "key", false);

    [Fact]
    public void A_full_valid_request_is_parsed()
    {
        var errors = SendNotificationValidator.Validate(Valid, out var request);

        errors.ShouldBeEmpty();
        request.ShouldNotBeNull();
        request.To.Select(h => h.ToString()).ShouldBe(["contact:alice", "group:family"]);
        request.Priority.ShouldBe(Priority.High);
        request.Channels.ShouldBe([ChannelType.Telegram, ChannelType.Sms]);
        (request.Title, request.Message, request.Tag, request.Source, request.IdempotencyKey).ShouldBe(("Title", "Message", "tag", "automation.x", "key"));
    }

    [Fact]
    public void Optional_fields_may_be_null_and_an_empty_channel_list_means_no_override()
    {
        var errors = SendNotificationValidator.Validate(Valid with { Title = null, Tag = null, Source = null, IdempotencyKey = null, Channels = [] }, out var request);

        errors.ShouldBeEmpty();
        request!.Channels.ShouldBeEmpty();
        SendNotificationValidator.Validate(Valid with { Channels = null }, out var noChannels).ShouldBeEmpty();
        noChannels!.Channels.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("missing message", NotifyFields.Message)]
    [InlineData("empty message", NotifyFields.Message)]
    [InlineData("long message", NotifyFields.Message)]
    [InlineData("missing to", NotifyFields.To)]
    [InlineData("empty to", NotifyFields.To)]
    [InlineData("malformed handle", NotifyFields.To)]
    [InlineData("null handle", NotifyFields.To)]
    [InlineData("missing priority", NotifyFields.Priority)]
    [InlineData("unknown priority", NotifyFields.Priority)]
    [InlineData("capitalised priority", NotifyFields.Priority)]
    [InlineData("unknown channel", NotifyFields.Channels)]
    [InlineData("capitalised channel", NotifyFields.Channels)]
    [InlineData("actions", NotifyFields.Actions)]
    [InlineData("long title", NotifyFields.Title)]
    [InlineData("long tag", NotifyFields.Tag)]
    [InlineData("long source", NotifyFields.Source)]
    [InlineData("long key", NotifyFields.IdempotencyKey)]
    public void AC10_Each_invalid_field_gets_a_field_error(string problem, string field)
    {
        var tooLong = new string('x', 201);
        var command = problem switch
        {
            "missing message" => Valid with { Message = null },
            "empty message" => Valid with { Message = "" },
            "long message" => Valid with { Message = new string('m', 4001) },
            "missing to" => Valid with { To = null },
            "empty to" => Valid with { To = [] },
            "malformed handle" => Valid with { To = ["alice"] },
            "null handle" => Valid with { To = [null] },
            "missing priority" => Valid with { Priority = null },
            "unknown priority" => Valid with { Priority = "urgent" },
            "capitalised priority" => Valid with { Priority = "High" },
            "unknown channel" => Valid with { Channels = ["email"] },
            "capitalised channel" => Valid with { Channels = ["SMS"] },
            "actions" => Valid with { HasActions = true },
            "long title" => Valid with { Title = tooLong },
            "long tag" => Valid with { Tag = tooLong },
            "long source" => Valid with { Source = tooLong },
            _ => Valid with { IdempotencyKey = tooLong },
        };

        var errors = SendNotificationValidator.Validate(command, out var request);

        request.ShouldBeNull();
        errors.Keys.ShouldBe([field]);
        errors[field].ShouldNotBeEmpty();
    }

    [Fact]
    public void Boundary_lengths_are_accepted()
    {
        var max = new string('x', 200);
        SendNotificationValidator.Validate(
            Valid with { Message = new string('m', 4000), Title = max, Tag = max, Source = max, IdempotencyKey = max }, out _).ShouldBeEmpty();
        SendNotificationValidator.Validate(Valid with { Message = "m" }, out _).ShouldBeEmpty();
    }

    [Fact]
    public void Every_problem_is_reported_at_once_with_one_message_per_bad_handle()
    {
        var errors = SendNotificationValidator.Validate(
            new SendNotificationCommand(["contact:alice", "bob", "group:"], null, null, "HIGH", ["fax"], null, null, null, true), out _);

        errors.Keys.ShouldBe([NotifyFields.To, NotifyFields.Message, NotifyFields.Priority, NotifyFields.Channels, NotifyFields.Actions], ignoreOrder: true);
        errors[NotifyFields.To].Length.ShouldBe(2);
        errors[NotifyFields.To][0].ShouldContain("'bob'");
    }

    [Theory]
    [InlineData("low", Priority.Low)]
    [InlineData("normal", Priority.Normal)]
    [InlineData("high", Priority.High)]
    [InlineData("critical", Priority.Critical)]
    public void Every_priority_name_parses(string name, Priority expected)
    {
        WireNames.TryParsePriority(name, out var priority).ShouldBeTrue();
        priority.ShouldBe(expected);
        WireNames.Of(expected).ShouldBe(name);
    }
}
