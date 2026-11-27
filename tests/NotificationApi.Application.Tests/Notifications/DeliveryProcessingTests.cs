using Microsoft.Extensions.Logging;
using NotificationApi.Application.Channels;
using NotificationApi.Application.Events;
using NotificationApi.Application.Notifications;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Tests.Notifications;

/// <summary><see cref="ProcessDueDeliveries"/>, <see cref="AttemptDelivery"/> and <see cref="RecoverInterruptedDeliveries"/> with fake time.</summary>
public sealed class DeliveryProcessingTests
{
    private readonly DeliveryHarness _h = new();

    [Fact]
    public async Task Claiming_moves_due_deliveries_to_sending_and_commits_before_any_send()
    {
        await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], "high"));
        _h.Journal.Steps.Clear();

        var due = await _h.Process.HandleAsync(CancellationToken.None);

        due.Claimed.Count.ShouldBe(2);
        due.NextDueAt.ShouldBeNull();
        _h.Store.Deliveries.ShouldAllBe(d => d.Status == DeliveryStatus.Sending && d.Attempts == 1);
        _h.Journal.Steps.ShouldBe(["save"]);
        _h.Telegram.Sent.ShouldBeEmpty();
        (await _h.Process.HandleAsync(CancellationToken.None)).Claimed.ShouldBeEmpty(); // not claimed twice
    }

    [Fact]
    public async Task AC01_A_sent_delivery_stores_the_provider_id_and_queues_delivery_sent_after_the_commit()
    {
        var receipt = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], tag: "front_door", source: "automation.door"));
        _h.Journal.Steps.Clear();

        await _h.RunWorkerOnceAsync();

        var delivery = _h.Delivery(receipt.Deliveries[0].DeliveryId);
        delivery.Status.ShouldBe(DeliveryStatus.Sent);
        delivery.ProviderMessageId.ShouldBe("msg-1");
        _h.Telegram.Sent.ShouldHaveSingleItem().Address.ShouldBe("1001");
        _h.Journal.Steps.ShouldBe(["save", "save", "enqueue delivery.sent"]);
        var haEvent = _h.Journal.Queued.ShouldHaveSingleItem();
        haEvent.Data.ShouldBe(new DeliverySentData(
            receipt.NotificationId.ToString(), delivery.Id.ToString(), "alice", "contact:alice", "telegram", "front_door", "automation.door"));
    }

    [Fact]
    public async Task AC20_A_channel_that_always_fails_gets_1_plus_3_retries_30_seconds_apart_then_fails()
    {
        _h.Telegram.Answer = (_, n) => ChannelResult.Failed($"Telegram error {n}");
        var start = _h.Time.GetUtcNow();
        await _h.SendAsync(DeliveryHarness.Command(["contact:bob"]));

        var attemptTimes = new List<DateTimeOffset>();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var due = await _h.RunWorkerOnceAsync();
            due.Claimed.Count.ShouldBe(1, $"attempt {attempt} should be due at {_h.Time.GetUtcNow():O}");
            attemptTimes.Add(_h.Time.GetUtcNow());
            if (attempt < 4)
            {
                var delivery = _h.Store.Deliveries.Single();
                delivery.Status.ShouldBe(DeliveryStatus.Retrying);
                delivery.NextAttemptAt.ShouldBe(_h.Time.GetUtcNow() + TimeSpan.FromSeconds(30));
                (await _h.Process.HandleAsync(CancellationToken.None)).NextDueAt.ShouldBe(delivery.NextAttemptAt);

                _h.Time.Advance(TimeSpan.FromSeconds(29));
                (await _h.Process.HandleAsync(CancellationToken.None)).Claimed.ShouldBeEmpty("not before 30 s");
                _h.Time.Advance(TimeSpan.FromSeconds(1));
            }
        }

        attemptTimes.ShouldBe([start, start.AddSeconds(30), start.AddSeconds(60), start.AddSeconds(90)]);
        var failed = _h.Store.Deliveries.Single();
        failed.Status.ShouldBe(DeliveryStatus.Failed);
        failed.Attempts.ShouldBe(4);
        failed.LastError.ShouldBe("Telegram error 4");
        _h.Telegram.Sent.Count.ShouldBe(4);
        var haEvent = _h.Journal.Queued.ShouldHaveSingleItem();
        haEvent.Type.ShouldBe(HaEventTypes.DeliveryFailed);
        ((DeliveryFailedData)haEvent.Data).Error.ShouldBe("Telegram error 4");
        _h.Time.Advance(TimeSpan.FromHours(1));
        (await _h.RunWorkerOnceAsync()).Claimed.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC20_The_retry_count_and_interval_come_from_the_options()
    {
        _h.Options.MaxRetries = 1;
        _h.Options.RetryInterval = TimeSpan.FromSeconds(5);
        _h.Telegram.Answer = (_, _) => ChannelResult.Failed("down");
        await _h.SendAsync(DeliveryHarness.Command(["contact:bob"]));

        await _h.RunWorkerOnceAsync();
        _h.Time.Advance(TimeSpan.FromSeconds(5));
        await _h.RunWorkerOnceAsync();

        _h.Store.Deliveries.Single().Status.ShouldBe(DeliveryStatus.Failed);
        _h.Telegram.Sent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AC21_A_channel_that_fails_once_then_succeeds_ends_sent_with_2_attempts()
    {
        _h.Telegram.Answer = (_, n) => n == 1 ? ChannelResult.Failed("flaky") : ChannelResult.Sent("ok-2");
        await _h.SendAsync(DeliveryHarness.Command(["contact:bob"]));

        await _h.RunWorkerOnceAsync();
        _h.Time.Advance(TimeSpan.FromSeconds(30));
        await _h.RunWorkerOnceAsync();

        var delivery = _h.Store.Deliveries.Single();
        (delivery.Status, delivery.Attempts, delivery.ProviderMessageId, delivery.LastError).ShouldBe((DeliveryStatus.Sent, 2, "ok-2", "flaky"));
        _h.Journal.Queued.ShouldHaveSingleItem().Type.ShouldBe(HaEventTypes.DeliverySent);
    }

    [Fact]
    public async Task A_retry_wakes_the_worker_so_it_can_plan_the_next_attempt()
    {
        _h.Telegram.Answer = (_, _) => ChannelResult.Failed("down");
        await _h.SendAsync(DeliveryHarness.Command(["contact:bob"]));
        await _h.WakeUp.WaitAsync(TimeSpan.Zero, _h.Time, CancellationToken.None); // consume the "queued" signal

        await _h.RunWorkerOnceAsync();

        _h.WakeUp.WaitAsync(TimeSpan.FromHours(1), _h.Time, CancellationToken.None).IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task AC24_AC25_Each_channel_gets_its_rendered_text()
    {
        await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], "high", title: "Door <1>", message: "Open & <script>"));

        await _h.RunWorkerOnceAsync();

        var telegram = _h.Telegram.Sent.ShouldHaveSingleItem();
        telegram.Text.ShouldBe("<b>Door &lt;1&gt;</b>\nOpen &amp; &lt;script&gt;");
        (telegram.Title, telegram.Body).ShouldBe(("Door <1>", "Open & <script>"));
        _h.Sms.Sent.ShouldHaveSingleItem().Text.ShouldBe("Door <1>: Open & <script>");
    }

    [Fact]
    public async Task AC25_Sms_without_a_title_is_just_the_message()
    {
        await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], "high", title: null, message: "Hello"));

        await _h.RunWorkerOnceAsync();

        _h.Sms.Sent.ShouldHaveSingleItem().Text.ShouldBe("Hello");
        _h.Telegram.Sent.ShouldHaveSingleItem().Text.ShouldBe("Hello");
    }

    [Fact]
    public async Task AC26_One_failing_delivery_does_not_affect_the_others_of_the_notification()
    {
        _h.Telegram.Answer = (_, _) => ChannelResult.Failed("Telegram is down");
        var receipt = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], "high"));

        await _h.RunWorkerOnceAsync();

        _h.Delivery(receipt.Deliveries[0].DeliveryId).Status.ShouldBe(DeliveryStatus.Retrying);
        _h.Delivery(receipt.Deliveries[1].DeliveryId).Status.ShouldBe(DeliveryStatus.Sent);
        _h.Journal.Queued.ShouldHaveSingleItem().Type.ShouldBe(HaEventTypes.DeliverySent);
    }

    [Fact]
    public async Task A_channel_with_no_implementation_fails_at_once_with_a_clear_error()
    {
        _h.Channels = [_h.Sms]; // no Telegram channel registered
        await _h.SendAsync(DeliveryHarness.Command(["contact:bob"]));

        await _h.RunWorkerOnceAsync();

        var delivery = _h.Store.Deliveries.Single();
        (delivery.Status, delivery.Attempts).ShouldBe((DeliveryStatus.Failed, 1));
        delivery.LastError.ShouldBe("The Telegram channel isn't available in this version of the add-on.");
        ((DeliveryFailedData)_h.Journal.Queued.Single().Data).Error.ShouldBe(delivery.LastError);
    }

    [Fact]
    public async Task A_channel_that_throws_counts_as_a_failed_attempt()
    {
        _h.Telegram.Answer = (_, _) => throw new InvalidOperationException("bug with +353861234567");
        await _h.SendAsync(DeliveryHarness.Command(["contact:bob"]));

        await _h.RunWorkerOnceAsync();

        var delivery = _h.Store.Deliveries.Single();
        delivery.Status.ShouldBe(DeliveryStatus.Retrying);
        delivery.LastError.ShouldBe("The Telegram channel failed unexpectedly (InvalidOperationException).");
        _h.AttemptLog.Entries.ShouldContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task An_attempt_for_a_delivery_that_is_not_claimed_does_nothing()
    {
        var receipt = await _h.SendAsync(DeliveryHarness.Command(["contact:bob"]));

        (await _h.Attempt.HandleAsync(receipt.Deliveries[0].DeliveryId, CancellationToken.None)).ShouldBeNull();
        (await _h.Attempt.HandleAsync(DeliveryId.New(_h.Time.GetUtcNow()), CancellationToken.None)).ShouldBeNull();
        _h.Telegram.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC74_Attempt_logs_never_hold_the_number_or_the_message_body()
    {
        _h.Sms.Answer = (_, _) => ChannelResult.Failed("rejected");
        await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], "high", title: "Secret title", message: "Secret body"));

        await _h.RunWorkerOnceAsync();

        _h.AttemptLog.Entries.ShouldNotBeEmpty();
        _h.AttemptLog.Entries.ShouldAllBe(e => !e.Text.Contains(DeliveryHarness.AliceSms) && !e.Text.Contains("Secret"));
        _h.AttemptLog.Entries.ShouldContain(e => e.Text.Contains("+3538****567"));
    }

    [Fact]
    public async Task AC23_Recovery_fails_interrupted_sends_without_resending_and_leaves_waiting_deliveries_alone()
    {
        _h.Telegram.Answer = (_, _) => ChannelResult.Failed("down");
        await _h.SendAsync(DeliveryHarness.Command(["contact:bob"], tag: "t")); // becomes retrying
        await _h.RunWorkerOnceAsync();
        var interrupted = await _h.SendAsync(DeliveryHarness.Command(["contact:alice"])); // claimed, then "crash"
        await _h.Process.HandleAsync(CancellationToken.None);
        await _h.SendAsync(DeliveryHarness.Command(["contact:alice"], "high")); // queued
        _h.Journal.Queued.Clear();
        _h.Telegram.Sent.Clear();

        var recovered = await _h.Recover.HandleAsync(CancellationToken.None);

        recovered.ShouldBe(1);
        var failed = _h.Delivery(interrupted.Deliveries[0].DeliveryId);
        failed.Status.ShouldBe(DeliveryStatus.Failed);
        failed.LastError.ShouldBe(RecoverInterruptedDeliveries.InterruptedError);
        _h.Journal.Queued.ShouldHaveSingleItem().Type.ShouldBe(HaEventTypes.DeliveryFailed);
        _h.Store.Deliveries.Count(d => d.Status == DeliveryStatus.Retrying).ShouldBe(1);
        _h.Store.Deliveries.Count(d => d.Status == DeliveryStatus.Queued).ShouldBe(2);
        _h.Telegram.Sent.ShouldBeEmpty();
        (await _h.Recover.HandleAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task Abandoning_a_crashed_attempt_fails_it_with_an_event_and_ignores_finished_ones()
    {
        var stuck = await _h.SendAsync(DeliveryHarness.Command(["contact:bob"]));
        await _h.Process.HandleAsync(CancellationToken.None); // claimed, then the attempt "crashes"
        _h.Journal.Steps.Clear();

        var abandoned = await _h.Abandon.HandleAsync(stuck.Deliveries[0].DeliveryId, CancellationToken.None);

        abandoned.ShouldBeTrue();
        var failed = _h.Delivery(stuck.Deliveries[0].DeliveryId);
        failed.Status.ShouldBe(DeliveryStatus.Failed);
        failed.LastError.ShouldBe(AbandonDelivery.AbandonedError);
        _h.Journal.Steps.ShouldBe(["save", $"enqueue {HaEventTypes.DeliveryFailed}"]);
        _h.Telegram.Sent.ShouldBeEmpty();
        (await _h.Abandon.HandleAsync(stuck.Deliveries[0].DeliveryId, CancellationToken.None)).ShouldBeFalse("already final");
    }

    [Fact]
    public async Task The_claim_batch_is_bounded()
    {
        for (var i = 0; i < ProcessDueDeliveries.BatchSize + 5; i++)
        {
            await _h.SendAsync(DeliveryHarness.Command(["contact:bob"]));
        }

        (await _h.Process.HandleAsync(CancellationToken.None)).Claimed.Count.ShouldBe(ProcessDueDeliveries.BatchSize);
        var rest = await _h.Process.HandleAsync(CancellationToken.None);
        rest.Claimed.Count.ShouldBe(5);
    }
}

public sealed class DeliveryWakeUpTests
{
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider _time = new();
    private readonly DeliveryWakeUp _wakeUp = new();

    [Fact]
    public async Task Waiting_returns_when_notified()
    {
        var wait = _wakeUp.WaitAsync(TimeSpan.FromHours(1), _time, TestContext.Current.CancellationToken);
        wait.IsCompleted.ShouldBeFalse();

        _wakeUp.Notify();

        await wait.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Waiting_returns_after_the_timeout_on_the_given_clock()
    {
        var wait = _wakeUp.WaitAsync(TimeSpan.FromSeconds(30), _time, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(20, TestContext.Current.CancellationToken);
        wait.IsCompleted.ShouldBeFalse();

        _time.Advance(TimeSpan.FromSeconds(1));

        await wait.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Repeated_signals_collapse_into_one()
    {
        _wakeUp.Notify();
        _wakeUp.Notify();

        await _wakeUp.WaitAsync(TimeSpan.FromHours(1), _time, TestContext.Current.CancellationToken);
        _wakeUp.WaitAsync(TimeSpan.FromHours(1), _time, TestContext.Current.CancellationToken).IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelling_the_wait_throws()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Should.ThrowAsync<OperationCanceledException>(() => _wakeUp.WaitAsync(TimeSpan.FromHours(1), _time, cts.Token));
    }
}
