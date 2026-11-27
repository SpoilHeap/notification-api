using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Infrastructure.Persistence.Configurations;

internal sealed class ApiClientConfiguration : IEntityTypeConfiguration<ApiClient>
{
    public void Configure(EntityTypeBuilder<ApiClient> builder)
    {
        builder.ToTable("ApiClients");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new ApiClientId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.Name).HasMaxLength(ApiClient.MaxNameLength).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();

        builder.Property(c => c.TokenHash)
            .HasConversion(hash => hash.ToArray(), bytes => ApiTokenHash.FromBytes(bytes))
            .HasMaxLength(ApiTokenHash.SizeInBytes)
            .IsRequired();

        builder.Property(c => c.ProtectedToken).IsRequired();
        builder.Property(c => c.WebhookUrl).HasMaxLength(2048);
        builder.Property(c => c.ProtectedWebhookSecret);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.LastUsedAt);
    }
}
