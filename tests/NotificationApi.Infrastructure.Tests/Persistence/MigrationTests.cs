using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationApi.Infrastructure.Persistence;

namespace NotificationApi.Infrastructure.Tests.Persistence;

public sealed class MigrationTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "notification-api-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Model_has_no_pending_changes()
    {
        // Fails when an entity or configuration changed without `dotnet ef migrations add` (README "Database migrations").
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        db.Database.HasPendingModelChanges().ShouldBeFalse("run: dotnet ef migrations add <Name> (see README)");
    }

    [Fact]
    public async Task Migrations_apply_cleanly_to_an_empty_database()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(TestContext.Current.CancellationToken);
        await using var db = database.CreateContext();

        (await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).ShouldNotBeEmpty();
        (await db.ApiClients.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Startup_migration_creates_the_database_in_the_data_path_and_is_idempotent()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataPath"] = _dataPath })
                .Build())
            .BuildServiceProvider();

        await provider.MigrateDatabaseAsync(TestContext.Current.CancellationToken);
        await provider.MigrateDatabaseAsync(TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(_dataPath, "notification-api.db")).ShouldBeTrue();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        (await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }
}
