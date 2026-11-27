using Microsoft.EntityFrameworkCore;
using NotificationApi.Domain.ApiClients;
using NotificationApi.Infrastructure.Persistence;

namespace NotificationApi.Infrastructure.Tests.Persistence;

public sealed class ApiClientRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public async Task Round_trips_an_api_client()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(ct);
        var token = ApiToken.Generate();
        var client = ApiClient.Create("home_assistant", token, "protected-token", Now);

        await using (var db = database.CreateContext())
        {
            new ApiClientRepository(db).Add(client);
            await new EfUnitOfWork(db).SaveChangesAsync(ct);
        }

        await using (var db = database.CreateContext())
        {
            var loaded = (await new ApiClientRepository(db).FindByNameAsync("home_assistant", ct)).ShouldNotBeNull();
            loaded.Id.ShouldBe(client.Id);
            loaded.Accepts(token).ShouldBeTrue();
            loaded.ProtectedToken.ShouldBe("protected-token");
            loaded.CreatedAt.ShouldBe(Now);
            loaded.LastUsedAt.ShouldBeNull();
            loaded.WebhookUrl.ShouldBeNull();
            (await new ApiClientRepository(db).FindByNameAsync("someone_else", ct)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task Changes_to_last_used_are_saved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(ct);
        await using (var db = database.CreateContext())
        {
            db.ApiClients.Add(ApiClient.Create("home_assistant", ApiToken.Generate(), "p", Now));
            await db.SaveChangesAsync(ct);
        }

        await using (var db = database.CreateContext())
        {
            var client = (await new ApiClientRepository(db).ListAsync(ct)).ShouldHaveSingleItem();
            client.RecordUse(Now.AddHours(1)).ShouldBeTrue();
            await new EfUnitOfWork(db).SaveChangesAsync(ct);
        }

        await using (var db = database.CreateContext())
        {
            (await db.ApiClients.SingleAsync(ct)).LastUsedAt.ShouldBe(Now.AddHours(1));
        }
    }

    [Fact]
    public async Task Names_are_unique()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(ct);
        await using var db = database.CreateContext();
        db.ApiClients.Add(ApiClient.Create("home_assistant", ApiToken.Generate(), "p", Now));
        db.ApiClients.Add(ApiClient.Create("home_assistant", ApiToken.Generate(), "p", Now));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }
}
