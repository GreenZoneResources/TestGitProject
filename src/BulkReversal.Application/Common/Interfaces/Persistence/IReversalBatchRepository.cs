using BulkReversal.Domain.Entities;

namespace BulkReversal.Application.Common.Interfaces.Persistence;

public interface IReversalBatchRepository
{
    Task<ReversalBatch?> GetByIdAsync(Guid id, bool includeTransactions, CancellationToken ct = default);
    Task<ReversalBatch?> GetByReferenceAsync(string batchReference, bool includeTransactions, CancellationToken ct = default);
    Task<bool> ReferenceExistsAsync(string batchReference, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string batchName, CancellationToken ct = default);
    void Add(ReversalBatch batch);

    Task<(IReadOnlyList<ReversalBatch> Items, int TotalCount)> SearchAsync(
        string? batchReferenceContains,
        Domain.Enums.BatchStatus? status,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReversalBatch>> GetRecentAsync(int count, CancellationToken ct = default);

    Task<DashboardCounts> GetDashboardCountsAsync(CancellationToken ct = default);
}

public record DashboardCounts(int Submitted, int PendingProcessing, int Reversed, int RejectedNeedsReview);
