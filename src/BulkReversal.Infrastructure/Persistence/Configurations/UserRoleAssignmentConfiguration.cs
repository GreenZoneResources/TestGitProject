using BulkReversal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BulkReversal.Infrastructure.Persistence.Configurations;

public class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.ToTable("UserRoleAssignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.UserId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.UserName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Email).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Role).HasMaxLength(100).IsRequired();

        builder.Property(a => a.AssignedByUserId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.AssignedByName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.RevokedByUserId).HasMaxLength(100);
        builder.Property(a => a.RevokedByName).HasMaxLength(200);
        builder.Property(a => a.CreatedBy).HasMaxLength(100);
        builder.Property(a => a.UpdatedBy).HasMaxLength(100);

        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.Role);

        // Guards against two concurrent "assign" requests double-granting the same active role —
        // mirrors the filtered-unique-index pattern already used for active reversal references.
        builder.HasIndex(a => new { a.UserId, a.Role })
            .HasDatabaseName("IX_UserRoleAssignments_UserId_Role_Active")
            .IsUnique()
            .HasFilter("[IsActive] = 1");
    }
}
