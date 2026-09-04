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

        builder.HasIndex(t => t.SessionIdOrFtReference);
        builder.HasIndex(t => t.BatchId);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.BatchReference);
        builder.HasIndex(t => new { t.RowValidationStatus, t.Status });

        // BRU-04: a reference must never have more than one "active" row system-wide — active
        // meaning already released to the engine or reversed (Submitted/Processing/Reversed), or
        // still a live valid row awaiting a decision (Status IS NULL AND RowValidationStatus =
        // 'Valid'). Application-layer checks (BatchUploadService, ApprovalService) are the primary,
        // UX-facing gate; this filtered unique index is the true last line of defense against a race
        // between two concurrent creates/edits/approvals — SQL Server only, ignored by the InMemory
        // provider used for local smoke-testing.
        builder.HasIndex(t => t.SessionIdOrFtReference)
            .HasDatabaseName("IX_ReversalTransactions_SessionIdOrFtReference_Active")
            .IsUnique()
            .HasFilter("[Status] IN (N'Submitted', N'Processing', N'Reversed') OR ([Status] IS NULL AND [RowValidationStatus] = N'Valid')");
    }
}
