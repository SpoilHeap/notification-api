using Microsoft.EntityFrameworkCore;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Infrastructure.Persistence;

/// <summary>
/// SQLite database in the add-on's <c>/data</c> volume. Entities are mapped by the
/// <see cref="IEntityTypeConfiguration{TEntity}"/> classes in <c>Persistence/Configurations</c>.
/// </summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
    }
}
