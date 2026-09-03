using BulkReversal.Domain.Enums;

namespace BulkReversal.Application.Features.Approvals.Dtos;

public record ApprovalListItemDto(
    Guid BatchId,
    string BatchReference,
    string SubmittedByName,
    int ValidRecords,
    DateTimeOffset? SubmittedAt);

public record ApprovalTransactionRowDto(
    int RowNumber,
    TransactionType TransactionType,
    string SessionIdOrFtReference,
    string? Rrn,
    string AccountNumber,
    decimal TransactionAmount,
    string? BeneficiaryBank,
    string? Biller);

public record ApprovalDetailDto(
    Guid BatchId,
    string BatchReference,
    string SubmittedByName,
    DateTimeOffset? SubmittedAt,
    int RecordsInBatch,
    BatchStatus Status,
    IReadOnlyList<ApprovalTransactionRowDto> Transactions,
    int Page,
    int PageSize,
    int TotalCount);

public record ApprovalDecisionRequest(string? Reason);
