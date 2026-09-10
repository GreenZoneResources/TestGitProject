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

    public async Task<DashboardCounts> GetDashboardCountsAsync(CancellationToken ct = default)
    {
        // "Submitted" is a rolling monthly activity count (how many rows were released to the
        // engine so far this calendar month), everything else is a live snapshot of current state
        // (how many are sitting in that state right now, regardless of when they got there).
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        // A row's Status flips to Submitted at the moment its batch is approved (ReversalBatch.Approve
        // -> ReversalTransaction.Submit), so the batch's DecidedAt is that row's "submitted at" —
        // ReversalTransaction itself doesn't carry a dedicated timestamp for that specific transition.
        var submittedThisMonth = await _db.ReversalTransactions
            .AsNoTracking()
            .Where(t => t.Status == ReversalStatus.Submitted)
            .Join(_db.ReversalBatches, t => t.BatchId, b => b.Id, (t, b) => b.DecidedAt)
            .CountAsync(decidedAt => decidedAt != null && decidedAt >= monthStart, ct);

        var statusCounts = await _db.ReversalTransactions
            .AsNoTracking()
            .Where(t => t.Status == ReversalStatus.Processing || t.Status == ReversalStatus.Reversed || t.Status == ReversalStatus.Rejected)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key!.Value, Count = g.Count() })
            .ToListAsync(ct);

        int Count(ReversalStatus status) => statusCounts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        return new DashboardCounts(
            Submitted: submittedThisMonth,
            PendingProcessing: Count(ReversalStatus.Processing),
            Reversed: Count(ReversalStatus.Reversed),
            RejectedNeedsReview: Count(ReversalStatus.Rejected));
    }
}
