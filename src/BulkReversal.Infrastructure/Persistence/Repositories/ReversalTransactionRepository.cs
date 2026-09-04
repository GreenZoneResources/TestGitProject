using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BulkReversal.Infrastructure.Persistence.Repositories;

public class ReversalTransactionRepository : IReversalTransactionRepository
{
    private readonly BulkReversalDbContext _db;

    public ReversalTransactionRepository(BulkReversalDbContext db) => _db = db;

    public Task<ReversalTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.ReversalTransactions.FirstOrDefaultAsync(t => t.Id == id, ct);

    // A row in Reversed/Submitted/Processing, or still a live (valid, undecided) row staged in
    // another batch, blocks a new reversal for the same reference (BRU-04). Rejected is deliberately
    // excluded — Settlement may legitimately resubmit after a rejection. Inlined into both queries
    // below (rather than a shared predicate) because EF Core's LINQ translator needs the condition
    // written directly in the query expression to turn it into SQL.
    public Task<bool> HasActiveConflictAsync(string sessionIdOrFtReference, Guid? excludeTransactionId, CancellationToken ct = default) =>
        _db.ReversalTransactions.AnyAsync(t =>
            t.SessionIdOrFtReference == sessionIdOrFtReference
            && (excludeTransactionId == null || t.Id != excludeTransactionId.Value)
            && (t.Status == ReversalStatus.Reversed || t.Status == ReversalStatus.Submitted || t.Status == ReversalStatus.Processing
                || (t.Status == null && t.RowValidationStatus == RowValidationStatus.Valid)),
            ct);

    public async Task<HashSet<string>> GetActiveConflictReferencesAsync(IEnumerable<string> sessionIdOrFtReferences, Guid? excludeBatchId = null, CancellationToken ct = default)
    {
        var candidates = sessionIdOrFtReferences.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0) return [];

        var matches = await _db.ReversalTransactions
            .AsNoTracking()
            .Where(t => candidates.Contains(t.SessionIdOrFtReference)
                && (excludeBatchId == null || t.BatchId != excludeBatchId.Value)
                && (t.Status == ReversalStatus.Reversed || t.Status == ReversalStatus.Submitted || t.Status == ReversalStatus.Processing
                    || (t.Status == null && t.RowValidationStatus == RowValidationStatus.Valid)))
            .Select(t => t.SessionIdOrFtReference)
            .ToListAsync(ct);

        return matches.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<ReversalTransaction>> GetPendingForEngineAsync(int maxItems, CancellationToken ct = default) =>
        await _db.ReversalTransactions
            .Where(t => t.Status == ReversalStatus.Submitted || t.Status == ReversalStatus.Processing)
            .OrderBy(t => t.CreatedAt)
            .Take(Math.Max(1, maxItems))
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<ReversalTransaction> Items, int TotalCount)> SearchAsync(
        string? batchReference,
        ReversalStatus? status,
        TransactionType? transactionType,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.ReversalTransactions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(batchReference))
            query = query.Where(t => t.BatchReference == batchReference);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (transactionType.HasValue)
            query = query.Where(t => t.TransactionType == transactionType.Value);

        if (fromDate.HasValue)
            query = query.Where(t => t.TransactionDate >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(t => t.TransactionDate <= toDate.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip(Math.Max(0, (page - 1) * pageSize))
            .Take(Math.Max(1, pageSize))
            .ToListAsync(ct);

        return (items, total);
    }
}
