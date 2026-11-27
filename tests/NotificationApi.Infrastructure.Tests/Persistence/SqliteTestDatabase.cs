using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationApi.Infrastructure.Persistence;

namespace NotificationApi.Infrastructure.Tests.Persistence;

/// <summary>Real SQLite in memory. The shared connection keeps the database alive across contexts (ADR 0005).</summary>
internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");

    private SqliteTestDatabase()
    {
    }

    public static async Task<SqliteTestDatabase> CreateMigratedAsync(CancellationToken cancellationToken)
    {
        var database = new SqliteTestDatabase();
        await database._connection.OpenAsync(cancellationToken);
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync(cancellationToken);
        return database;
    }

    public NotificationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>().UseSqlite(_connection).Options);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
