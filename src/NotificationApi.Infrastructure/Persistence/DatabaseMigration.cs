using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NotificationApi.Infrastructure.Persistence;

public static partial class DatabaseMigration
{
    /// <summary>Applies pending migrations. Safe on startup: the add-on is a single instance (ADR 0005).</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigration));

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        await db.Database.MigrateAsync(cancellationToken);

        if (pending.Count == 0)
        {
            LogUpToDate(logger);
        }
        else
        {
            var names = string.Join(", ", pending);
            LogApplied(logger, pending.Count, names);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} database migration(s): {Migrations}.")]
    private static partial void LogApplied(ILogger logger, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema is up to date.")]
    private static partial void LogUpToDate(ILogger logger);
}
