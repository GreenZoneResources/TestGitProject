using BulkReversal.Domain.Enums;

namespace BulkReversal.Application.Features.Upload.Dtos;

/// <summary>Command to stage and validate a new batch upload (FR-03). ASP.NET-free by design:
/// the API layer copies the incoming IFormFile into <see cref="FileStream"/> before invoking this.</summary>
public record UploadBatchCommand(string BatchName, Stream FileStream, string FileName, long FileSizeBytes);

public record InvalidRowDto(int RowNumber, string? SessionIdOrFtReference, IReadOnlyList<string> Errors);

public record UploadBatchResultDto(
    Guid BatchId,
    string BatchReference,
    string BatchName,
    BatchStatus Status,
    int TotalRecords,
    int ValidRecords,
    int InvalidRecords,
    IReadOnlyList<InvalidRowDto> InvalidRows);

/// <summary>One row of the staging table (Upload results screen), including the fields the
/// Edit-Record dialog lets Settlement correct.</summary>
public record TransactionRowDto(
    Guid TransactionId,
    int RowNumber,
    TransactionType TransactionType,
    string SessionIdOrFtReference,
    string? Rrn,
    string AccountNumber,
    DateOnly TransactionDate,
    decimal TransactionAmount,
    string Channel,
    string? BeneficiaryBank,
    string? Biller,
    string ReasonForFailure,
    string? Comments,
    RowValidationStatus RowValidationStatus,
    IReadOnlyList<string> ValidationErrors);

/// <summary>Partial update for a single staged row — only supplied (non-null) fields are changed;
/// everything else keeps its current value. Sent from the Upload results screen's Edit Record dialog.</summary>
public record EditTransactionRowRequest(
    TransactionType? TransactionType = null,
    string? SessionIdOrFtReference = null,
    string? Rrn = null,
    string? AccountNumber = null,
    DateOnly? TransactionDate = null,
    decimal? TransactionAmount = null,
    string? Channel = null,
    string? BeneficiaryBank = null,
    string? Biller = null,
    string? ReasonForFailure = null,
    string? Comments = null);
