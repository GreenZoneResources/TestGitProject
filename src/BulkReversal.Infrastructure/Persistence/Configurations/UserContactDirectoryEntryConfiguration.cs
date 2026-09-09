using BulkReversal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BulkReversal.Infrastructure.Persistence.Configurations;

public class UserContactDirectoryEntryConfiguration : IEntityTypeConfiguration<UserContactDirectoryEntry>
{
    public void Configure(EntityTypeBuilder<UserContactDirectoryEntry> builder)
    {
        builder.ToTable("UserContactDirectory");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.UserId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.UserName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Email).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Role).HasMaxLength(100).IsRequired();
        builder.Property(a => a.CreatedBy).HasMaxLength(100);
        builder.Property(a => a.UpdatedBy).HasMaxLength(100);

        builder.HasIndex(a => a.Role);
        builder.HasIndex(a => new { a.UserId, a.Role }).IsUnique();
    }
}
