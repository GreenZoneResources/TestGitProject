namespace BulkReversal.Application.Features.Upload.Dtos;

/// <summary>
/// A single row exactly as read off the Reversal Upload Template (BRD Section 6), before type
/// conversion or business validation. All fields are raw strings so the parser never has to guess
/// at intent — that's the validator's job.
/// </summary>
public class RawUploadRow
{
    public int RowNumber { get; init; }
    public string? SerialNumber { get; init; }
    public string? TransactionType { get; init; }
    public string? SessionIdOrFtReference { get; init; }
    public string? Rrn { get; init; }
    public string? AccountNumber { get; init; }
    public string? TransactionDate { get; init; }
    public string? TransactionAmount { get; init; }
    public string? Channel { get; init; }
    public string? BeneficiaryBank { get; init; }
    public string? Biller { get; init; }
    public string? ReasonForFailure { get; init; }
    public string? Comments { get; init; }
}
