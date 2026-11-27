using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationApi.Domain.Instances;

namespace NotificationApi.Infrastructure.Persistence.Configurations;

internal sealed class InstanceConfiguration : IEntityTypeConfiguration<Instance>
{
    public void Configure(EntityTypeBuilder<Instance> builder)
    {
        builder.ToTable("Instance");
        builder.HasKey(i => i.Id);

        // Stored as the lower-case text the API returns, so the row reads the same as GET /ping.
        builder.Property(i => i.Id)
            .HasConversion(id => id.ToString(), value => InstanceId.Parse(value))
            .HasMaxLength(36)
            .ValueGeneratedNever();

        // A concurrency token: two changes racing to bump the revision can't both save the same new value.
        builder.Property(i => i.RecipientsRevisionNumber)
            .HasColumnName("RecipientsRevision")
            .IsRequired()
            .IsConcurrencyToken();
        builder.Ignore(i => i.RecipientsRevision);

        builder.Property(i => i.CreatedAt).IsRequired();
    }
}
