using NotificationApi.Domain.Audit;
using NotificationApi.Infrastructure.Persistence;

namespace NotificationApi.Infrastructure.Tests.Persistence;

public sealed class AuditLogRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AC82_Entries_round_trip_newest_first_and_page()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(ct);
        var actor = new AuditActor("8f7e6d5c4b3a", "Alice Admin");

        await using (var db = database.CreateContext())
        {
            var repository = new AuditLogRepository(db);
            for (var i = 0; i < 5; i++)
            {
                repository.Add(AuditEntry.Record(actor, AuditAction.Update, AuditEntityType.Endpoint, "contact:bob", $"SMS number set to +35386123456{i}", Now.AddMinutes(i)));
            }

            await db.SaveChangesAsync(ct);
        }

        await using (var db = database.CreateContext())
        {
            var repository = new AuditLogRepository(db);
            (await repository.CountAsync(ct)).ShouldBe(5);
            var page = await repository.ListNewestFirstAsync(1, 2, ct);
            page.Select(e => e.OccurredAt).ShouldBe([Now.AddMinutes(3), Now.AddMinutes(2)]);
            var entry = page[0];
            entry.HaUserId.ShouldBe("8f7e6d5c4b3a");
            entry.HaUserDisplayName.ShouldBe("Alice Admin");
            entry.Action.ShouldBe(AuditAction.Update);
            entry.EntityType.ShouldBe(AuditEntityType.Endpoint);
            entry.EntityKey.ShouldBe("contact:bob");
            entry.Summary.ShouldBe("SMS number set to +3538****563");
            entry.Id.ShouldBeGreaterThan(0);
        }
    }
}
