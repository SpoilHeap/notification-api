using Microsoft.EntityFrameworkCore;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;
using NotificationApi.Domain.Settings;
using NotificationApi.Infrastructure.Persistence;

namespace NotificationApi.Infrastructure.Tests.Persistence;

/// <summary>The persisted delivery queue (ADR 0014) on real SQLite.</summary>
public sealed class NotificationRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 16, 15, 20, 123, TimeSpan.Zero);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_notification_and_its_deliveries_round_trip()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        var (notification, deliveries) = NewNotification("k1", Now, 2);

        await using (var db = database.CreateContext())
        {
            new NotificationRepository(db).Add(notification, deliveries);
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            var repository = new NotificationRepository(db);
            var loaded = (await repository.FindAsync(notification.Id, _ct)).ShouldNotBeNull();
            (loaded.Title, loaded.Message, loaded.Priority, loaded.Tag, loaded.Source, loaded.IdempotencyKey, loaded.CreatedAt)
                .ShouldBe(("Door", "Opened", Priority.High, "tag", "automation.x", "k1", Now));
            loaded.ChannelOverride.ShouldBe([ChannelType.Sms, ChannelType.Telegram]);
            var list = await repository.ListDeliveriesAsync(notification.Id, _ct);
            list.Select(d => (d.Id, d.Position, d.Contact, d.Via, d.Channel, d.Address, d.Status, d.NextAttemptAt))
                .ShouldBe(deliveries.Select(d => (d.Id, d.Position, d.Contact, d.Via, d.Channel, d.Address, DeliveryStatus.Queued, (DateTimeOffset?)Now)));
            (await repository.FindDeliveryAsync(deliveries[1].Id, _ct)).ShouldNotBeNull().Position.ShouldBe(1);
        }
    }

    [Fact]
    public async Task State_changes_persist()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        var (notification, deliveries) = NewNotification(null, Now, 1);
        await SaveAsync(database, notification, deliveries);

        await using (var db = database.CreateContext())
        {
            var delivery = (await new NotificationRepository(db).FindDeliveryAsync(deliveries[0].Id, _ct))!;
            delivery.StartAttempt(Now);
            delivery.MarkSent("11ec-832f", Now.AddSeconds(1));
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            var delivery = (await new NotificationRepository(db).FindDeliveryAsync(deliveries[0].Id, _ct))!;
            (delivery.Status, delivery.Attempts, delivery.ProviderMessageId, delivery.CompletedAt, delivery.NextAttemptAt)
                .ShouldBe((DeliveryStatus.Sent, 1, "11ec-832f", (DateTimeOffset?)Now.AddSeconds(1), (DateTimeOffset?)null));
        }
    }

    [Fact]
    public async Task Due_deliveries_are_queued_or_retrying_ones_whose_time_has_come_earliest_first()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        var (late, lateDeliveries) = NewNotification(null, Now.AddSeconds(10), 1);
        var (early, earlyDeliveries) = NewNotification(null, Now, 3);
        await SaveAsync(database, late, lateDeliveries);
        await SaveAsync(database, early, earlyDeliveries);
        await using (var db = database.CreateContext())
        {
            var repository = new NotificationRepository(db);
            var retrying = (await repository.FindDeliveryAsync(earlyDeliveries[1].Id, _ct))!;
            retrying.StartAttempt(Now);
            retrying.RecordFailedAttempt("x", Now, RetryPolicy.Default); // due at +30 s
            var sending = (await repository.FindDeliveryAsync(earlyDeliveries[2].Id, _ct))!;
            sending.StartAttempt(Now);
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            var repository = new NotificationRepository(db);
            (await repository.ListDueDeliveriesAsync(Now.AddSeconds(5), 10, _ct)).Select(d => d.Id).ShouldBe([earlyDeliveries[0].Id]);
            (await repository.ListDueDeliveriesAsync(Now.AddSeconds(30), 10, _ct)).Select(d => d.Id)
                .ShouldBe([earlyDeliveries[0].Id, lateDeliveries[0].Id, earlyDeliveries[1].Id]);
            (await repository.ListDueDeliveriesAsync(Now.AddSeconds(30), 2, _ct)).Count.ShouldBe(2);
            (await repository.NextAttemptAtAsync(_ct)).ShouldBe(Now);
            (await repository.ListDeliveriesInStatusAsync(DeliveryStatus.Sending, _ct)).Select(d => d.Id).ShouldBe([earlyDeliveries[2].Id]);
        }
    }

    [Fact]
    public async Task Nothing_waiting_means_no_next_attempt()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        await using var db = database.CreateContext();

        (await new NotificationRepository(db).NextAttemptAtAsync(_ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_idempotency_lookup_finds_the_newest_match_within_the_window()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        var (old, oldDeliveries) = NewNotification("K", Now.AddHours(-30), 1);
        var (recent, recentDeliveries) = NewNotification("K", Now.AddHours(-2), 1);
        var (other, otherDeliveries) = NewNotification("other", Now, 1);
        await SaveAsync(database, old, oldDeliveries);
        await SaveAsync(database, recent, recentDeliveries);
        await SaveAsync(database, other, otherDeliveries);
        await using var db = database.CreateContext();
        var repository = new NotificationRepository(db);

        (await repository.FindByIdempotencyKeyAsync("K", Now.AddHours(-24), _ct))!.Id.ShouldBe(recent.Id);
        (await repository.FindByIdempotencyKeyAsync("K", Now.AddHours(-48), _ct))!.Id.ShouldBe(recent.Id);
        (await repository.FindByIdempotencyKeyAsync("K", Now.AddHours(-1), _ct)).ShouldBeNull();
        (await repository.FindByIdempotencyKeyAsync("missing", Now.AddHours(-48), _ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_a_notification_deletes_its_deliveries()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        var (notification, deliveries) = NewNotification(null, Now, 2);
        await SaveAsync(database, notification, deliveries);

        await using var db = database.CreateContext();
        await db.Notifications.Where(n => n.Id == notification.Id).ExecuteDeleteAsync(_ct);

        (await db.Deliveries.CountAsync(_ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Settings_round_trip_as_a_single_row()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        await using (var db = database.CreateContext())
        {
            var repository = new SettingsRepository(db);
            (await repository.GetAsync(_ct)).ShouldBeNull();
            var settings = AppSettings.CreateDefault(Now);
            settings.SetSmsTo("CfDJ8-protected", "WXYZ", "Home", Now);
            repository.Add(settings);
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            var loaded = (await new SettingsRepository(db).GetAsync(_ct)).ShouldNotBeNull();
            (loaded.SmsToProtectedApiKey, loaded.SmsToApiKeyHint, loaded.SmsToSenderId, loaded.SmsToConfigured).ShouldBe(("CfDJ8-protected", "WXYZ", "Home", true));
        }
    }

    private static (Notification, IReadOnlyList<Delivery>) NewNotification(string? key, DateTimeOffset at, int count)
    {
        var notification = Notification.Create("Door", "Opened", Priority.High, [ChannelType.Sms, ChannelType.Telegram], "tag", "automation.x", key, at);
        var deliveries = Enumerable.Range(0, count)
            .Select(i => Delivery.Create(notification.Id, i, Handle.Contact("alice"), Handle.Group("family"), ChannelType.Sms, "+353861234567", at))
            .ToList();
        return (notification, deliveries);
    }

    private async Task SaveAsync(SqliteTestDatabase database, Notification notification, IReadOnlyList<Delivery> deliveries)
    {
        await using var db = database.CreateContext();
        new NotificationRepository(db).Add(notification, deliveries);
        await db.SaveChangesAsync(_ct);
    }
}
