using Microsoft.EntityFrameworkCore;
using NotificationApi.Domain.ApiClients;
using NotificationApi.Domain.Instances;
using NotificationApi.Infrastructure.Persistence;

namespace NotificationApi.Infrastructure.Tests.Persistence;

public sealed class InstanceRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Round_trips_the_instance_and_its_revision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(ct);
        var instance = Instance.Create(Now);

        await using (var db = database.CreateContext())
        {
            var repository = new InstanceRepository(db);
            (await repository.GetAsync(ct)).ShouldBeNull();
            repository.Add(instance);
            await new EfUnitOfWork(db).SaveChangesAsync(ct);
        }

        await using (var db = database.CreateContext())
        {
            var loaded = (await new InstanceRepository(db).GetAsync(ct)).ShouldNotBeNull();
            loaded.Id.ShouldBe(instance.Id);
            loaded.RecipientsRevision.ShouldBe("1");
            loaded.CreatedAt.ShouldBe(Now);
            loaded.BumpRecipientsRevision();
            await db.SaveChangesAsync(ct);
        }

        await using (var db = database.CreateContext())
        {
            (await new InstanceRepository(db).GetAsync(ct)).ShouldNotBeNull().RecipientsRevision.ShouldBe("2");
            // Stored as the lower-case text the API returns.
            var stored = await db.Database.SqlQueryRaw<string>("SELECT Id AS Value FROM Instance").SingleAsync(ct);
            stored.ShouldBe(instance.Id.ToString());
        }
    }

    [Fact]
    public async Task Concurrent_revision_bumps_cannot_both_save()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(ct);
        await using (var db = database.CreateContext())
        {
            db.Instances.Add(Instance.Create(Now));
            await db.SaveChangesAsync(ct);
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        (await new InstanceRepository(first).GetAsync(ct))!.BumpRecipientsRevision();
        (await new InstanceRepository(second).GetAsync(ct))!.BumpRecipientsRevision();

        await first.SaveChangesAsync(ct);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Webhook_registrations_round_trip_and_clients_are_found_by_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(ct);
        var client = ApiClient.Create("home_assistant", ApiToken.Generate(), "protected-token", Now);
        client.RegisterWebhook(new Uri("http://homeassistant:8123/api/webhook/abc"), "protected-secret", Now.AddMinutes(1));

        await using (var db = database.CreateContext())
        {
            new ApiClientRepository(db).Add(client);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = database.CreateContext())
        {
            var repository = new ApiClientRepository(db);
            var loaded = (await repository.FindByIdAsync(client.Id, ct)).ShouldNotBeNull();
            loaded.WebhookUrl.ShouldBe(new Uri("http://homeassistant:8123/api/webhook/abc"));
            loaded.ProtectedWebhookSecret.ShouldBe("protected-secret");
            loaded.WebhookRegisteredAt.ShouldBe(Now.AddMinutes(1));
            (await repository.FindByIdAsync(ApiClientId.New(), ct)).ShouldBeNull();
            loaded.RemoveWebhook();
            await db.SaveChangesAsync(ct);
        }

        await using (var db = database.CreateContext())
        {
            (await new ApiClientRepository(db).FindByIdAsync(client.Id, ct)).ShouldNotBeNull().HasWebhook.ShouldBeFalse();
        }
    }
}
