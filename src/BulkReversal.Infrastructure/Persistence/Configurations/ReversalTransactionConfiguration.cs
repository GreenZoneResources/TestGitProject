using BulkReversal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BulkReversal.Infrastructure.Persistence.Configurations;

public class ReversalTransactionConfiguration : IEntityTypeConfiguration<ReversalTransaction>
{
    public void Configure(EntityTypeBuilder<ReversalTransaction> builder)
    {
        builder.ToTable("ReversalTransactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.BatchReference).HasMaxLength(50).IsRequired();
        builder.Property(t => t.TransactionType).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.SessionIdOrFtReference).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Rrn).HasMaxLength(50);
        builder.Property(t => t.AccountNumber).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Channel).HasMaxLength(50).IsRequired();
        builder.Property(t => t.BeneficiaryBank).HasMaxLength(100);
        builder.Property(t => t.Biller).HasMaxLength(100);
        builder.Property(t => t.ReasonForFailure).HasMaxLength(500).IsRequired();
        builder.Property(t => t.Comments).HasMaxLength(1000);
        builder.Property(t => t.ValidationErrors).HasMaxLength(2000);
        builder.Property(t => t.ExternalReference).HasMaxLength(128);
        builder.Property(t => t.FailureCode).HasMaxLength(64);
        builder.Property(t => t.FailureReason).HasMaxLength(1024);
        builder.Property(t => t.CreatedBy).HasMaxLength(100);
        builder.Property(t => t.UpdatedBy).HasMaxLength(100);

        builder.Property(t => t.TransactionAmount).HasColumnType("decimal(18,2)");

        builder.Property(t => t.RowValidationStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);

        // BRU-04: an already-reversed ftReference must never be reversed again. Enforced defensively
        // at the application layer (bulk pre-check) and here as the last line of defense against a
        // race between two concurrent uploads/approvals.
        builder.HasIndex(t => t.SessionIdOrFtReference);
        builder.HasIndex(t => t.BatchId);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.BatchReference);
        builder.HasIndex(t => new { t.RowValidationStatus, t.Status });
    }
}
