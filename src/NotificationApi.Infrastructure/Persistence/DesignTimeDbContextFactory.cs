using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotificationApi.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only, so the tools don't boot the web host (whose data path is <c>/data</c>).
/// See README "Database migrations".
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
