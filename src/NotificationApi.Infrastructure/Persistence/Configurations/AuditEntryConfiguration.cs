using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditLog");
        builder.HasKey(e => e.Id);

        // SQLite can't order by DateTimeOffset, so newest-first uses this insertion-ordered key.
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.HaUserId).HasMaxLength(AuditActor.MaxLength).IsRequired();
        builder.Property(e => e.HaUserDisplayName).HasMaxLength(AuditActor.MaxLength).IsRequired();
        builder.Property(e => e.Action).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.EntityType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.EntityKey).HasMaxLength(AuditEntry.MaxEntityKeyLength).IsRequired();
        builder.Property(e => e.Summary).HasMaxLength(AuditEntry.MaxSummaryLength).IsRequired();
    }
}
