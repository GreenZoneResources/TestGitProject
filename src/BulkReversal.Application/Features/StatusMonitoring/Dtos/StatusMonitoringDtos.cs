using BulkReversal.Domain.Enums;

namespace BulkReversal.Application.Features.StatusMonitoring.Dtos;

public record DashboardCountsDto(int Submitted, int PendingProcessing, int Reversed, int RejectedNeedsReview);

public record RecentBatchDto(string BatchReference, string UploadedByName, int Records, DateTimeOffset UploadedAt, BatchStatus Status);

public record DashboardDto(DashboardCountsDto Counts, IReadOnlyList<RecentBatchDto> RecentBatches);

public record StatusFilter(
    string? BatchReference,
    DateOnly? FromDate,
    DateOnly? ToDate,
    ReversalStatus? Status,
    TransactionType? TransactionType,
    int Page = 1,
    int PageSize = 20);

public record TransactionStatusDto(
    string SessionIdOrFtReference,
    TransactionType TransactionType,
    decimal TransactionAmount,
    string BatchReference,
    ReversalStatus? Status,
    string? ExternalReference,
    string? FailureReason,
    DateTimeOffset LastUpdated);

public enum ExportFormat
{
    Excel = 1,
    Pdf = 2
}
