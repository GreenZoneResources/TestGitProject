using BulkReversal.Domain.Common;
using BulkReversal.Domain.Enums;
using BulkReversal.Domain.Exceptions;

namespace BulkReversal.Domain.Entities;

/// <summary>
/// A single failed-transaction reversal request, one row of the Reversal Upload Template
/// (BRD Section 6). <see cref="SessionIdOrFtReference"/> is the idempotency key used both for
/// BRU-04 (no double reversal) and as the provider-contract "ftReference".
/// </summary>
public class ReversalTransaction : AuditableEntity
{
    public Guid BatchId { get; private set; }

    /// <summary>Denormalized from the owning batch at creation time (immutable) so status-monitoring
    /// queries can filter/display by reference without a join.</summary>
    public string BatchReference { get; private set; } = string.Empty;

    public int RowNumber { get; private set; }

    public TransactionType TransactionType { get; private set; }

    /// <summary>Unique transaction identifier used for revalidation (Col C). Idempotency key.</summary>
    public string SessionIdOrFtReference { get; private set; } = string.Empty;

    /// <summary>Required for card transactions (Col D).</summary>
    public string? Rrn { get; private set; }

    public string AccountNumber { get; private set; } = string.Empty;
    public DateOnly TransactionDate { get; private set; }
    public decimal TransactionAmount { get; private set; }
    public string Channel { get; private set; } = string.Empty;

    /// <summary>Required for NIP transactions (Col I).</summary>
    public string? BeneficiaryBank { get; private set; }

    /// <summary>Required for bill payment transactions (Col J).</summary>
    public string? Biller { get; private set; }

    public string ReasonForFailure { get; private set; } = string.Empty;
    public string? Comments { get; private set; }

    public RowValidationStatus RowValidationStatus { get; private set; }
    public string? ValidationErrors { get; private set; }

    public ReversalStatus? Status { get; private set; }

    public DateTimeOffset? FirstPolledAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureReason { get; private set; }

    private ReversalTransaction() { }

    public static ReversalTransaction Create(
        Guid batchId,
        string batchReference,
        int rowNumber,
        TransactionType transactionType,
        string sessionIdOrFtReference,
        string? rrn,
        string accountNumber,
        DateOnly transactionDate,
        decimal transactionAmount,
        string channel,
        string? beneficiaryBank,
        string? biller,
        string reasonForFailure,
        string? comments,
        string createdBy)
    {
        return new ReversalTransaction
        {
            BatchId = batchId,
            BatchReference = batchReference,
            RowNumber = rowNumber,
            TransactionType = transactionType,
            SessionIdOrFtReference = sessionIdOrFtReference.Trim(),
            Rrn = string.IsNullOrWhiteSpace(rrn) ? null : rrn.Trim(),
            AccountNumber = accountNumber.Trim(),
            TransactionDate = transactionDate,
            TransactionAmount = transactionAmount,
            Channel = channel.Trim(),
            BeneficiaryBank = string.IsNullOrWhiteSpace(beneficiaryBank) ? null : beneficiaryBank.Trim(),
            Biller = string.IsNullOrWhiteSpace(biller) ? null : biller.Trim(),
            ReasonForFailure = reasonForFailure.Trim(),
            Comments = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim(),
            RowValidationStatus = RowValidationStatus.Invalid,
            CreatedBy = createdBy
        };
    }

    public void MarkValid()
    {
        RowValidationStatus = RowValidationStatus.Valid;
        ValidationErrors = null;
    }

    public void MarkInvalid(IEnumerable<string> reasons)
    {
        RowValidationStatus = RowValidationStatus.Invalid;
        ValidationErrors = string.Join("; ", reasons);
    }

    /// <summary>
    /// In-place correction of a staged row (Settlement fixing a Failed record before submission).
    /// Refuses once the row has entered the reversal pipeline (<see cref="Status"/> assigned) —
    /// the owning <see cref="ReversalBatch.EnsureMutable"/> gate is the primary guard, this is
    /// defense-in-depth at the row level. Callers must re-run validation and call
    /// <see cref="MarkValid"/>/<see cref="MarkInvalid"/> themselves afterward.
    /// </summary>
    public void EditFields(
        TransactionType transactionType,
        string sessionIdOrFtReference,
        string? rrn,
        string accountNumber,
        DateOnly transactionDate,
        decimal transactionAmount,
        string channel,
        string? beneficiaryBank,
        string? biller,
        string reasonForFailure,
        string? comments,
        string updatedBy)
    {
        if (Status is not null)
            throw new DomainException($"Row {RowNumber} ({SessionIdOrFtReference}) can no longer be edited; it has already been submitted for processing.");

        TransactionType = transactionType;
        SessionIdOrFtReference = sessionIdOrFtReference.Trim();
        Rrn = string.IsNullOrWhiteSpace(rrn) ? null : rrn.Trim();
        AccountNumber = accountNumber.Trim();
        TransactionDate = transactionDate;
        TransactionAmount = transactionAmount;
        Channel = channel.Trim();
        BeneficiaryBank = string.IsNullOrWhiteSpace(beneficiaryBank) ? null : beneficiaryBank.Trim();
        Biller = string.IsNullOrWhiteSpace(biller) ? null : biller.Trim();
        ReasonForFailure = reasonForFailure.Trim();
        Comments = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
        Touch(updatedBy);
    }

    /// <summary>Approved and staged; eligible for pickup by the reversal engine's polling endpoint.</summary>
    public void Submit()
    {
        if (RowValidationStatus != RowValidationStatus.Valid)
            throw new DomainException($"Row {RowNumber} cannot be submitted: it is not a valid record.");

        Status = ReversalStatus.Submitted;
    }

    /// <summary>The reversal engine polled this item via GET pending-reversals (§1, provider contract).</summary>
    public void MarkRetrievedByEngine()
    {
        if (Status != ReversalStatus.Submitted && Status != ReversalStatus.Processing)
            throw new DomainException($"Row {RowNumber} ({SessionIdOrFtReference}) is not eligible for engine pickup (status: {Status}).");

        FirstPolledAt ??= DateTimeOffset.UtcNow;
        Status = ReversalStatus.Processing;
    }

    /// <summary>Records the outcome delivered via POST callback (§2.3, provider contract). Idempotent by design.</summary>
    public void ApplyCallback(bool isSuccessful, string? externalReference, string? failureCode, string? failureReason)
    {
        if (Status is ReversalStatus.Reversed or ReversalStatus.Rejected)
        {
            // §2.5: repeat delivery for an already-terminal msgId must be a safe no-op.
            return;
        }

        ProcessedAt = DateTimeOffset.UtcNow;
        ExternalReference = externalReference;

        if (isSuccessful)
        {
            Status = ReversalStatus.Reversed;
            FailureCode = null;
            FailureReason = null;
        }
        else
        {
            Status = ReversalStatus.Rejected;
            FailureCode = failureCode;
            FailureReason = failureReason;
        }
    }

    /// <summary>Stable, opaque correlation id exposed to the reversal engine as "msgId".</summary>
    public string MsgId => Id.ToString("N");

    public DateTimeOffset LastUpdated => ProcessedAt ?? FirstPolledAt ?? UpdatedAt ?? CreatedAt;
}
