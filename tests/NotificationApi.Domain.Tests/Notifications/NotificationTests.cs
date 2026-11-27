using NotificationApi.Domain.Notifications;

namespace NotificationApi.Domain.Tests.Notifications;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 16, 15, 20, TimeSpan.Zero);

    [Fact]
    public void Ids_are_prefixed_lower_case_hex_of_a_version_7_uuid()
    {
        var notification = Notification.Create(null, "m", Priority.Normal, [], null, null, null, Now);

        var text = notification.Id.ToString();
        text.ShouldMatch("^ntf_[0-9a-f]{32}$");
        notification.Id.Value.Version.ShouldBe(7);
        NotificationId.TryParse(text, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(notification.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ntf_")]
    [InlineData("dlv_019ac63f8e407c3a9d1b2e4f5a6b7c8d")]
    [InlineData("ntf_019AC63F8E407C3A9D1B2E4F5A6B7C8D")]
    [InlineData("ntf_019ac63f-8e40-7c3a-9d1b-2e4f5a6b7c8d")]
    public void Malformed_ids_do_not_parse(string? value)
    {
        NotificationId.TryParse(value, out _).ShouldBeFalse();
    }

    [Fact]
    public void Delivery_ids_round_trip()
    {
        var id = DeliveryId.New(Now);

        DeliveryId.TryParse(id.ToString(), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
        DeliveryId.TryParse("ntf_" + id.Value.ToString("N"), out _).ShouldBeFalse();
    }

    [Fact]
    public void Create_keeps_the_request_and_drops_duplicate_override_channels()
    {
        var n = Notification.Create("Front door", "Opened", Priority.High, [ChannelType.Sms, ChannelType.Sms], "door", "automation.door", "k1", Now);

        (n.Title, n.Message, n.Priority, n.Tag, n.Source, n.IdempotencyKey, n.CreatedAt)
            .ShouldBe(("Front door", "Opened", Priority.High, "door", "automation.door", "k1", Now));
        n.ChannelOverride.ShouldBe([ChannelType.Sms]);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(null, "title")]
    public void Invalid_values_throw(string? message, string? overLongField)
    {
        var tooLong = new string('x', 201);
        Should.Throw<ArgumentException>(() => Notification.Create(
            overLongField == "title" ? tooLong : null, message ?? "m", Priority.Normal, [], null, null, null, Now));
    }

    [Fact]
    public void Retry_policy_bounds_are_enforced()
    {
        RetryPolicy.Default.MaxAttempts.ShouldBe(4);
        RetryPolicy.Default.Interval.ShouldBe(TimeSpan.FromSeconds(30));
        Should.Throw<ArgumentOutOfRangeException>(() => new RetryPolicy(-1, TimeSpan.FromSeconds(1)));
        Should.Throw<ArgumentOutOfRangeException>(() => new RetryPolicy(1, TimeSpan.Zero));
    }
}

/// <summary>AC24, AC25.</summary>
public sealed class MessageFormatterTests
{
    [Fact]
    public void AC24_Telegram_is_bold_title_newline_message_with_html_escaped()
    {
        MessageFormatter.Render(ChannelType.Telegram, "Door <b>& co", "<script>alert(1)</script> & \"x\"")
            .ShouldBe("<b>Door &lt;b&gt;&amp; co</b>\n&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;x&quot;");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AC24_Telegram_without_a_title_is_just_the_escaped_message(string? title)
    {
        MessageFormatter.Render(ChannelType.Telegram, title, "a < b").ShouldBe("a &lt; b");
    }

    [Fact]
    public void AC25_Sms_is_title_colon_message_in_plain_text()
    {
        MessageFormatter.Render(ChannelType.Sms, "Front door", "Opened <now> & then").ShouldBe("Front door: Opened <now> & then");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AC25_Sms_without_a_title_is_just_the_message(string? title)
    {
        MessageFormatter.Render(ChannelType.Sms, title, "Opened").ShouldBe("Opened");
    }
}
