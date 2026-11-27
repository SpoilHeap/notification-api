using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasConversion<NotificationIdConverter>().ValueGeneratedNever();
        builder.Property(n => n.Title).HasMaxLength(Notification.MaxTitleLength);
        builder.Property(n => n.Message).HasMaxLength(Notification.MaxMessageLength).IsRequired();
        builder.Property(n => n.Priority).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(n => n.ChannelOverride)
            .HasConversion(new ChannelListConverter(), ChannelListConverter.Comparer)
            .HasMaxLength(ChannelListConverter.MaxLength)
            .IsRequired();
        builder.Property(n => n.Tag).HasMaxLength(Notification.MaxTextLength);
        builder.Property(n => n.Source).HasMaxLength(Notification.MaxTextLength);
        builder.Property(n => n.IdempotencyKey).HasMaxLength(Notification.MaxTextLength);
        builder.Property(n => n.CreatedAt).HasConversion<UnixMillisecondsConverter>().IsRequired();

        // Not unique: after 24 h the same key counts as new (AC12), so the newest match within the window wins.
        builder.HasIndex(n => new { n.IdempotencyKey, n.CreatedAt });
        builder.HasIndex(n => n.CreatedAt); // the retention purge (AC73)
    }
}

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("Deliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasConversion<DeliveryIdConverter>().ValueGeneratedNever();
        builder.Property(d => d.NotificationId).HasConversion<NotificationIdConverter>().IsRequired();
        builder.HasOne<Notification>().WithMany().HasForeignKey(d => d.NotificationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => new { d.NotificationId, d.Position }).IsUnique();

        // Handles as text, not foreign keys: a delivery is a record of what happened, and survives the contact.
        builder.Property(d => d.Contact).HasConversion<HandleConverter>().HasMaxLength(HandleConverter.MaxLength).IsRequired();
        builder.Property(d => d.Via).HasConversion<HandleConverter>().HasMaxLength(HandleConverter.MaxLength).IsRequired();
        builder.Property(d => d.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.Address).HasMaxLength(ContactConfiguration.MaxAddressLength).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.Attempts).IsRequired();
        builder.Property(d => d.LastError).HasMaxLength(Delivery.MaxErrorLength);
        builder.Property(d => d.ProviderMessageId).HasMaxLength(Delivery.MaxProviderMessageIdLength);
        builder.Property(d => d.NextAttemptAt).HasConversion<UnixMillisecondsConverter>();
        builder.Property(d => d.CreatedAt).HasConversion<UnixMillisecondsConverter>().IsRequired();
        builder.Property(d => d.LastAttemptAt).HasConversion<UnixMillisecondsConverter>();
        builder.Property(d => d.CompletedAt).HasConversion<UnixMillisecondsConverter>();
        builder.Ignore(d => d.MaskedAddress);
        builder.Ignore(d => d.IsFinal);

        // The worker's query: due deliveries by status and time.
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt });
    }
}
