using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Infrastructure.Persistence.Configurations;

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    /// <summary>E.164: '+' and at most 15 digits, with room for a Telegram chat id.</summary>
    public const int MaxAddressLength = 64;

    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("Contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        // Unique per type: contacts and groups have their own tables, so contact:x and group:x can coexist (AC57).
        builder.Property(c => c.Handle).HasConversion<HandleConverter>().HasMaxLength(HandleConverter.MaxLength).IsRequired();
        builder.HasIndex(c => c.Handle).IsUnique();

        builder.Property(c => c.Name).HasConversion<DisplayNameConverter>().HasMaxLength(DisplayName.MaxLength).IsRequired();
        builder.Property(c => c.Routing)
            .HasConversion(new RoutingConverter(), RoutingConverter.Comparer)
            .HasMaxLength(RoutingConverter.MaxLength)
            .IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Ignore(c => c.SmsNumber);

        builder.OwnsMany(c => c.Endpoints, endpoint =>
        {
            endpoint.ToTable("ContactEndpoints");
            endpoint.WithOwner().HasForeignKey("ContactId");
            endpoint.Property(e => e.Channel).HasConversion<string>().HasMaxLength(16);
            endpoint.HasKey("ContactId", nameof(Endpoint.Channel)); // one endpoint per channel
            endpoint.Property(e => e.Address).HasMaxLength(MaxAddressLength).IsRequired();
            endpoint.Property(e => e.VerifiedAt);
            endpoint.Ignore(e => e.IsVerified);
            endpoint.Ignore(e => e.MaskedAddress);
        });
    }
}
