using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BulkReversal.Infrastructure.Persistence.Repositories;

public class ReversalBatchRepository : IReversalBatchRepository
{
    private readonly BulkReversalDbContext _db;

    public ReversalBatchRepository(BulkReversalDbContext db) => _db = db;

    public async Task<ReversalBatch?> GetByIdAsync(Guid id, bool includeTransactions, CancellationToken ct = default)
    {
        var query = _db.ReversalBatches.AsQueryable();
        if (includeTransactions) query = query.Include(b => b.Transactions);
        return await query.FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<ReversalBatch?> GetByReferenceAsync(string batchReference, bool includeTransactions, CancellationToken ct = default)
    {
        var query = _db.ReversalBatches.AsQueryable();
        if (includeTransactions) query = query.Include(b => b.Transactions);
        return await query.FirstOrDefaultAsync(b => b.BatchReference == batchReference, ct);
    }

    public Task<bool> ReferenceExistsAsync(string batchReference, CancellationToken ct = default) =>
        _db.ReversalBatches.AnyAsync(b => b.BatchReference == batchReference, ct);

    public Task<bool> NameExistsAsync(string batchName, CancellationToken ct = default) =>
        _db.ReversalBatches.AnyAsync(b => b.BatchName == batchName, ct);

    public void Add(ReversalBatch batch) => _db.ReversalBatches.Add(batch);

    public async Task<(IReadOnlyList<ReversalBatch> Items, int TotalCount)> SearchAsync(
        string? batchReferenceContains,
        BatchStatus? status,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.ReversalBatches.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(batchReferenceContains))
            query = query.Where(b => b.BatchReference.Contains(batchReferenceContains));

        if (status.HasValue)
            query = query.Where(b => b.Status == status.Value);

        if (fromDate.HasValue)
        {
            var from = fromDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(b => b.UploadedAt >= from);
        }

        if (toDate.HasValue)
        {
            var to = toDate.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(b => b.UploadedAt <= to);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(b => b.UploadedAt)
            .Skip(Math.Max(0, (page - 1) * pageSize))
            .Take(Math.Max(1, pageSize))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<ReversalBatch>> GetRecentAsync(int count, CancellationToken ct = default) =>
        await _db.ReversalBatches
            .AsNoTracking()
            .OrderByDescending(b => b.UploadedAt)
            .Take(count)
            .ToListAsync(ct);

    public async Task<DashboardCounts> GetDashboardCountsAsync(CancellationToken ct = default)
    {
        var statusCounts = await _db.ReversalTransactions
            .AsNoTracking()
            .Where(t => t.Status != null)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key!.Value, Count = g.Count() })
            .ToListAsync(ct);

        int Count(ReversalStatus status) => statusCounts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        return new DashboardCounts(
            Submitted: Count(ReversalStatus.Submitted),
            PendingProcessing: Count(ReversalStatus.Processing),
            Reversed: Count(ReversalStatus.Reversed),
            RejectedNeedsReview: Count(ReversalStatus.Rejected));
    }
}
