using Microsoft.EntityFrameworkCore;

namespace NotificationApi.Infrastructure.Persistence;

/// <summary>
/// SQLite database in the add-on's <c>/data</c> volume. Entity configurations are added as
/// <see cref="IEntityTypeConfiguration{TEntity}"/> classes in this assembly as features land.
/// </summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
    }
}
