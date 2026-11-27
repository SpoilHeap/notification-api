using Microsoft.EntityFrameworkCore;
using NotificationApi.Domain.ApiClients;
using NotificationApi.Domain.Audit;
using NotificationApi.Domain.Instances;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;
using NotificationApi.Domain.Settings;
using NotificationApi.Infrastructure.Persistence.Configurations;

namespace NotificationApi.Infrastructure.Persistence;

/// <summary>
/// SQLite database in the add-on's <c>/data</c> volume. Entities are mapped by the
/// <see cref="IEntityTypeConfiguration{TEntity}"/> classes in <c>Persistence/Configurations</c>.
/// </summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    public DbSet<Instance> Instances => Set<Instance>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        // Strongly-typed ids, also where they're nullable foreign keys (group members).
        configurationBuilder.Properties<ContactId>().HaveConversion<ContactIdConverter>();
        configurationBuilder.Properties<GroupId>().HaveConversion<GroupIdConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
    }
}
