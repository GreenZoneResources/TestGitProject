using BulkReversal.Domain.Common;
using BulkReversal.Domain.Enums;
using BulkReversal.Domain.Exceptions;

namespace BulkReversal.Domain.Entities;

/// <summary>
/// A staged batch of failed-transaction reversal requests uploaded by the Settlement Team
/// (BRD 3.2-3.4). Owns the set of <see cref="ReversalTransaction"/> rows parsed from the file.
/// </summary>
public class ReversalBatch : AuditableEntity
{
    private readonly List<ReversalTransaction> _transactions = new();

    /// <summary>Human-friendly name supplied by the uploader.</summary>
    public string BatchName { get; private set; } = string.Empty;

    /// <summary>System-generated unique tracking reference, e.g. BR-20260805-01 (FR-09).</summary>
    public string BatchReference { get; private set; } = string.Empty;

    public string OriginalFileName { get; private set; } = string.Empty;

    public string UploadedByUserId { get; private set; } = string.Empty;
    public string UploadedByName { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; private set; }

    public BatchStatus Status { get; private set; } = BatchStatus.Uploaded;

    public int TotalRecords { get; private set; }
    public int ValidRecords { get; private set; }
    public int InvalidRecords { get; private set; }

    public string? SubmittedByUserId { get; private set; }
    public string? SubmittedByName { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }

    public string? DecidedByUserId { get; private set; }
    public string? DecidedByName { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionReason { get; private set; }

    public IReadOnlyCollection<ReversalTransaction> Transactions => _transactions.AsReadOnly();

    private ReversalBatch() { }

    public static ReversalBatch Create(string batchName, string originalFileName, string uploadedByUserId, string uploadedByName, string batchReference)
    {
        if (string.IsNullOrWhiteSpace(batchName))
            throw new DomainException("Batch name is required.");
        if (string.IsNullOrWhiteSpace(batchReference))
            throw new DomainException("Batch reference is required.");

        return new ReversalBatch
        {
            BatchName = batchName.Trim(),
            OriginalFileName = originalFileName,
            UploadedByUserId = uploadedByUserId,
            UploadedByName = uploadedByName,
            BatchReference = batchReference,
            UploadedAt = DateTimeOffset.UtcNow,
            Status = BatchStatus.Uploaded,
            CreatedBy = uploadedByUserId
        };
    }

    public void AddTransaction(ReversalTransaction transaction) => _transactions.Add(transaction);

    /// <summary>Records the outcome of upload-time validation (FR-06/FR-07).</summary>
    public void CompleteValidation()
    {
        TotalRecords = _transactions.Count;
        ValidRecords = _transactions.Count(t => t.RowValidationStatus == RowValidationStatus.Valid);
        InvalidRecords = TotalRecords - ValidRecords;
        Status = BatchStatus.Validated;
    }

    public void SubmitForApproval(string userId, string userName)
    {
        if (Status != BatchStatus.Validated)
            throw new DomainException($"Batch {BatchReference} cannot be submitted for approval from status {Status}.");
        if (ValidRecords == 0)
            throw new DomainException($"Batch {BatchReference} has no valid records to submit.");

        SubmittedByUserId = userId;
        SubmittedByName = userName;
        SubmittedAt = DateTimeOffset.UtcNow;
        Status = BatchStatus.PendingApproval;
        Touch(userId);
    }

    /// <summary>Authorizes the batch: valid rows become eligible for pickup by the reversal engine.</summary>
    public void Approve(string approverUserId, string approverName)
    {
        if (Status != BatchStatus.PendingApproval)
            throw new DomainException($"Batch {BatchReference} cannot be approved from status {Status}.");

        DecidedByUserId = approverUserId;
        DecidedByName = approverName;
        DecidedAt = DateTimeOffset.UtcNow;
        Status = BatchStatus.Approved;
        Touch(approverUserId);

        foreach (var txn in _transactions.Where(t => t.RowValidationStatus == RowValidationStatus.Valid))
        {
            txn.Submit();
        }
    }

    public void Reject(string approverUserId, string approverName, string reason)
    {
        if (Status != BatchStatus.PendingApproval)
            throw new DomainException($"Batch {BatchReference} cannot be rejected from status {Status}.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A rejection reason is required.");

        DecidedByUserId = approverUserId;
        DecidedByName = approverName;
        DecidedAt = DateTimeOffset.UtcNow;
        DecisionReason = reason.Trim();
        Status = BatchStatus.Rejected;
        Touch(approverUserId);
    }

    /// <summary>Recomputes whether every submitted row has reached a terminal state and closes the batch out.</summary>
    public void RefreshCompletionState()
    {
        if (Status != BatchStatus.Approved) return;

        var submittedRows = _transactions.Where(t => t.RowValidationStatus == RowValidationStatus.Valid).ToList();
        if (submittedRows.Count > 0 && submittedRows.All(t => t.Status is ReversalStatus.Reversed or ReversalStatus.Rejected))
        {
            Status = BatchStatus.Completed;
        }
    }
}
