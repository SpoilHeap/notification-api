using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Infrastructure.Persistence.Configurations;

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("Groups");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.Handle).HasConversion<HandleConverter>().HasMaxLength(HandleConverter.MaxLength).IsRequired();
        builder.HasIndex(g => g.Handle).IsUnique();

        builder.Property(g => g.Name).HasConversion<DisplayNameConverter>().HasMaxLength(DisplayName.MaxLength).IsRequired();
        builder.Property(g => g.CreatedAt).IsRequired();

        builder.OwnsMany(g => g.Members, member =>
        {
            member.ToTable("GroupMembers", table => table.HasCheckConstraint(
                "CK_GroupMembers_ExactlyOneMember",
                "(\"MemberContactId\" IS NULL) <> (\"MemberGroupId\" IS NULL)"));
            member.WithOwner().HasForeignKey("GroupId");
            member.Property<int>("Id").ValueGeneratedOnAdd();
            member.HasKey("Id");
            member.Ignore(m => m.IsContact);

            // Deleting a contact or group also deletes its memberships (AC56). The use cases remove them first, so
            // the audit log records each one; the cascade keeps the database consistent regardless.
            member.HasOne<Contact>().WithMany().HasForeignKey(m => m.MemberContactId).OnDelete(DeleteBehavior.Cascade);
            member.HasOne<Group>().WithMany().HasForeignKey(m => m.MemberGroupId).OnDelete(DeleteBehavior.Cascade);

            // No duplicate member in the same group. SQLite treats NULLs as distinct, so each index only
            // constrains rows of its own kind.
            member.HasIndex("GroupId", nameof(GroupMember.MemberContactId)).IsUnique();
            member.HasIndex("GroupId", nameof(GroupMember.MemberGroupId)).IsUnique();
        });
    }
}
