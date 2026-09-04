using BulkReversal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BulkReversal.Infrastructure.Persistence.Configurations;

public class ReversalBatchConfiguration : IEntityTypeConfiguration<ReversalBatch>
{
    public void Configure(EntityTypeBuilder<ReversalBatch> builder)
    {
        builder.ToTable("ReversalBatches");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.BatchName).HasMaxLength(200).IsRequired();
        builder.Property(b => b.BatchReference).HasMaxLength(50).IsRequired();
        builder.HasIndex(b => b.BatchReference).IsUnique();

        builder.Property(b => b.UploadedByUserId).HasMaxLength(100).IsRequired();
        builder.Property(b => b.UploadedByName).HasMaxLength(200).IsRequired();
        builder.Property(b => b.SubmittedByUserId).HasMaxLength(100);
        builder.Property(b => b.SubmittedByName).HasMaxLength(200);
        builder.Property(b => b.DecidedByUserId).HasMaxLength(100);
        builder.Property(b => b.DecidedByName).HasMaxLength(200);
        builder.Property(b => b.DecisionReason).HasMaxLength(500);
        builder.Property(b => b.CreatedBy).HasMaxLength(100);
        builder.Property(b => b.UpdatedBy).HasMaxLength(100);

        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(b => b.Status);
        builder.HasIndex(b => b.UploadedAt);

        builder.Metadata.FindNavigation(nameof(ReversalBatch.Transactions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.Transactions)
            .WithOne()
            .HasForeignKey(t => t.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
