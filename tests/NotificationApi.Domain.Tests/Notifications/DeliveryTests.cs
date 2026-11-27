using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Tests.Notifications;

/// <summary>The Delivery state machine (engineering_standards.md §2.2, §4.2): every legal and illegal transition.</summary>
public sealed class DeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 16, 15, 20, TimeSpan.Zero);
    private static readonly RetryPolicy Policy = RetryPolicy.Default;

    [Fact]
    public void A_new_delivery_is_queued_and_due_at_once()
    {
        var delivery = NewDelivery();

        delivery.Status.ShouldBe(DeliveryStatus.Queued);
        delivery.Attempts.ShouldBe(0);
        delivery.NextAttemptAt.ShouldBe(Now);
        delivery.IsDue(Now).ShouldBeTrue();
        delivery.IsDue(Now.AddSeconds(-1)).ShouldBeFalse();
        delivery.IsFinal.ShouldBeFalse();
        delivery.Id.ToString().ShouldStartWith("dlv_");
    }

    [Fact]
    public void Queued_to_sending_to_sent_stores_the_provider_message_id()
    {
        var delivery = NewDelivery();

        delivery.StartAttempt(Now);
        delivery.Status.ShouldBe(DeliveryStatus.Sending);
        delivery.Attempts.ShouldBe(1);
        delivery.NextAttemptAt.ShouldBeNull();
        delivery.IsDue(Now).ShouldBeFalse();

        delivery.MarkSent("11ec-832f", Now.AddSeconds(1));
        delivery.Status.ShouldBe(DeliveryStatus.Sent);
        delivery.ProviderMessageId.ShouldBe("11ec-832f");
        delivery.CompletedAt.ShouldBe(Now.AddSeconds(1));
        delivery.IsFinal.ShouldBeTrue();
    }

    [Fact]
    public void A_failed_attempt_with_retries_left_is_retrying_and_due_after_the_interval()
    {
        var delivery = NewDelivery();
        delivery.StartAttempt(Now);

        delivery.RecordFailedAttempt("timeout", Now, Policy);

        delivery.Status.ShouldBe(DeliveryStatus.Retrying);
        delivery.LastError.ShouldBe("timeout");
        delivery.NextAttemptAt.ShouldBe(Now.AddSeconds(30));
        delivery.IsDue(Now.AddSeconds(29)).ShouldBeFalse();
        delivery.IsDue(Now.AddSeconds(30)).ShouldBeTrue();
    }

    [Fact]
    public void AC20_The_fourth_failed_attempt_is_final()
    {
        var delivery = NewDelivery();
        var at = Now;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            delivery.StartAttempt(at);
            delivery.RecordFailedAttempt($"error {attempt}", at, Policy);
            delivery.Status.ShouldBe(DeliveryStatus.Retrying);
            at = delivery.NextAttemptAt!.Value;
        }

        delivery.StartAttempt(at);
        delivery.RecordFailedAttempt("error 4", at, Policy);

        delivery.Status.ShouldBe(DeliveryStatus.Failed);
        delivery.Attempts.ShouldBe(4);
        delivery.LastError.ShouldBe("error 4");
        delivery.NextAttemptAt.ShouldBeNull();
        delivery.CompletedAt.ShouldBe(Now.AddSeconds(90));
    }

    [Fact]
    public void With_no_retries_the_first_failure_is_final()
    {
        var delivery = NewDelivery();
        delivery.StartAttempt(Now);

        delivery.RecordFailedAttempt("nope", Now, new RetryPolicy(0, TimeSpan.FromSeconds(1)));

        delivery.Status.ShouldBe(DeliveryStatus.Failed);
    }

    [Fact]
    public void Retrying_can_start_another_attempt_and_then_succeed()
    {
        var delivery = NewDelivery();
        delivery.StartAttempt(Now);
        delivery.RecordFailedAttempt("once", Now, Policy);

        delivery.StartAttempt(Now.AddSeconds(30));
        delivery.MarkSent(null, Now.AddSeconds(31));

        delivery.Status.ShouldBe(DeliveryStatus.Sent);
        delivery.Attempts.ShouldBe(2);
        delivery.ProviderMessageId.ShouldBeNull();
    }

    [Fact]
    public void Fail_is_final_with_no_retry()
    {
        var delivery = NewDelivery();
        delivery.StartAttempt(Now);

        delivery.Fail("No channel.", Now);

        delivery.Status.ShouldBe(DeliveryStatus.Failed);
        delivery.Attempts.ShouldBe(1);
        delivery.LastError.ShouldBe("No channel.");
    }

    public static TheoryData<string, string> IllegalTransitions => new()
    {
        { "queued", "sent" }, { "queued", "failed-attempt" }, { "queued", "fail" },
        { "sending", "start" },
        { "retrying", "sent" }, { "retrying", "failed-attempt" }, { "retrying", "fail" },
        { "sent", "start" }, { "sent", "sent" }, { "sent", "failed-attempt" }, { "sent", "fail" },
        { "failed", "start" }, { "failed", "sent" }, { "failed", "failed-attempt" }, { "failed", "fail" },
    };

    [Theory]
    [MemberData(nameof(IllegalTransitions))]
    public void Illegal_transitions_throw(string from, string transition)
    {
        var delivery = InState(from);
        var before = delivery.Status;

        Action act = transition switch
        {
            "start" => () => delivery.StartAttempt(Now),
            "sent" => () => delivery.MarkSent("x", Now),
            "failed-attempt" => () => delivery.RecordFailedAttempt("x", Now, Policy),
            _ => () => delivery.Fail("x", Now),
        };

        Should.Throw<InvalidOperationException>(act);
        delivery.Status.ShouldBe(before);
    }

    [Fact]
    public void Errors_are_trimmed_truncated_and_never_hold_a_full_phone_number()
    {
        var delivery = NewDelivery();
        delivery.StartAttempt(Now);

        delivery.Fail("  Invalid number +353861234567 " + new string('x', 600), Now);

        delivery.LastError!.Length.ShouldBe(Delivery.MaxErrorLength);
        delivery.LastError.ShouldStartWith("Invalid number +3538****567 ");
    }

    [Fact]
    public void A_delivery_goes_to_a_contact_handle()
    {
        Should.Throw<ArgumentException>(() =>
            Delivery.Create(NotificationId.New(Now), 0, Handle.Group("family"), Handle.Group("family"), ChannelType.Sms, "+353861234567", Now));
    }

    [Fact]
    public void The_address_is_masked_for_logs()
    {
        NewDelivery().MaskedAddress.ShouldBe("+3538****567");
        Delivery.Create(NotificationId.New(Now), 0, Handle.Contact("bob"), Handle.Contact("bob"), ChannelType.Telegram, "123456", Now)
            .MaskedAddress.ShouldBe("(chat)");
    }

    private static Delivery NewDelivery() =>
        Delivery.Create(NotificationId.New(Now), 0, Handle.Contact("alice"), Handle.Group("family"), ChannelType.Sms, "+353861234567", Now);

    private static Delivery InState(string state)
    {
        var delivery = NewDelivery();
        switch (state)
        {
            case "sending":
                delivery.StartAttempt(Now);
                break;
            case "retrying":
                delivery.StartAttempt(Now);
                delivery.RecordFailedAttempt("x", Now, Policy);
                break;
            case "sent":
                delivery.StartAttempt(Now);
                delivery.MarkSent("x", Now);
                break;
            case "failed":
                delivery.StartAttempt(Now);
                delivery.Fail("x", Now);
                break;
        }

        return delivery;
    }
}
