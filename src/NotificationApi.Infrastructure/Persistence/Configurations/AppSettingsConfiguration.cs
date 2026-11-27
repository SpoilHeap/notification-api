using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationApi.Domain.Settings;

namespace NotificationApi.Infrastructure.Persistence.Configurations;

internal sealed class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.ToTable("Settings", table => table.HasCheckConstraint("CK_Settings_SingleRow", $"\"Id\" = {AppSettings.SingletonId}"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.SmsToProtectedApiKey); // Data Protection ciphertext (ADR 0012)
        builder.Property(s => s.SmsToApiKeyHint).HasMaxLength(8);
        builder.Property(s => s.SmsToSenderId).HasMaxLength(AppSettings.MaxSmsToSenderIdLength);
        builder.Property(s => s.UpdatedAt).IsRequired();
        builder.Ignore(s => s.SmsToConfigured);
    }
}
