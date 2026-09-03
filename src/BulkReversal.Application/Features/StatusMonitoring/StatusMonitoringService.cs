using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Common.Models;
using BulkReversal.Application.Features.StatusMonitoring.Dtos;
using BulkReversal.Domain.Entities;

namespace BulkReversal.Application.Features.StatusMonitoring;

public class StatusMonitoringService : IStatusMonitoringService
{
    private readonly IReversalBatchRepository _batchRepository;
    private readonly IReversalTransactionRepository _transactionRepository;
    private readonly IReportExportService _exportService;

    public StatusMonitoringService(
        IReversalBatchRepository batchRepository,
        IReversalTransactionRepository transactionRepository,
        IReportExportService exportService)
    {
        _batchRepository = batchRepository;
        _transactionRepository = transactionRepository;
        _exportService = exportService;
    }

    public async Task<DashboardDto> GetDashboardAsync(int recentBatchCount = 10, CancellationToken ct = default)
    {
        var counts = await _batchRepository.GetDashboardCountsAsync(ct);
        var recent = await _batchRepository.GetRecentAsync(recentBatchCount, ct);

        var recentDtos = recent
            .Select(b => new RecentBatchDto(b.BatchReference, b.UploadedByName, b.TotalRecords, b.UploadedAt, b.Status))
            .ToList();

        return new DashboardDto(
            new DashboardCountsDto(counts.Submitted, counts.PendingProcessing, counts.Reversed, counts.RejectedNeedsReview),
            recentDtos);
    }

    public async Task<PagedResult<TransactionStatusDto>> SearchAsync(StatusFilter filter, CancellationToken ct = default)
    {
        var (items, total) = await _transactionRepository.SearchAsync(
            filter.BatchReference, filter.Status, filter.TransactionType, filter.FromDate, filter.ToDate,
            filter.Page, filter.PageSize, ct);

        var dtos = items.Select(MapToDto).ToList();
        return PagedResult<TransactionStatusDto>.Create(dtos, filter.Page, filter.PageSize, total);
    }

    public async Task<byte[]> ExportAsync(StatusFilter filter, ExportFormat format, CancellationToken ct = default)
    {
        // Export the full filtered set (bounded by a generous page size), not just the current page.
        var (items, _) = await _transactionRepository.SearchAsync(
            filter.BatchReference, filter.Status, filter.TransactionType, filter.FromDate, filter.ToDate,
            page: 1, pageSize: 50_000, ct);

        var rows = items.Select(MapToDto).ToList();
        return _exportService.BuildStatusReport(rows, format);
    }

    private static TransactionStatusDto MapToDto(ReversalTransaction t) => new(
        t.SessionIdOrFtReference, t.TransactionType, t.TransactionAmount, t.BatchReference,
        t.Status, t.ExternalReference, t.FailureReason, t.LastUpdated);
}
