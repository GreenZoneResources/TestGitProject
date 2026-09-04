using BulkReversal.Domain.Enums;

namespace BulkReversal.Application.Features.Upload.Dtos;

/// <summary>
/// Request body for creating a staged reversal batch (FR-03). The frontend parses/collects the
/// reconciliation rows itself and posts them here as structured JSON — this app no longer parses
/// an uploaded file server-side.
/// </summary>
public class CreateReversalBatchRequest
{
    public string BatchName { get; set; } = string.Empty;

    public List<TransactionRevalidationRequest> Transactions { get; set; } = new();
}

/// <summary>One row of the Reversal Upload Template (BRD Section 6), already structured/typed by
/// the caller. <see cref="SessionIdOrFtReference"/> is revalidated against source transaction
/// records via the Transfer Service (BRU-05) in addition to the usual field/duplicate checks.</summary>
public class TransactionRevalidationRequest
{
    public TransactionType TransactionType { get; set; }

    /// <summary>Unique transaction identifier used for revalidation. Idempotency key.</summary>
    public string SessionIdOrFtReference { get; set; } = string.Empty;

    /// <summary>Required for card transactions.</summary>
    public string? Rrn { get; set; }

    public string AccountNumber { get; set; } = string.Empty;
    public DateOnly TransactionDate { get; set; }
    public decimal TransactionAmount { get; set; }
    public string Channel { get; set; } = string.Empty;

    /// <summary>Required for NIP transactions.</summary>
    public string? BeneficiaryBank { get; set; }

    /// <summary>Required for bill payment transactions.</summary>
    public string? Biller { get; set; }

    public string ReasonForFailure { get; set; } = string.Empty;
    public string? Comments { get; set; }
}
