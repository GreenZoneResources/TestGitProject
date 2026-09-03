using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BulkReversal.Infrastructure.Persistence.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly BulkReversalDbContext _db;

    public AuditLogRepository(BulkReversalDbContext db) => _db = db;

    public void Add(AuditLogEntry entry) => _db.AuditLogEntries.Add(entry);

    public async Task<(IReadOnlyList<AuditLogEntry> Items, int TotalCount)> SearchAsync(
        string? batchReference, string? entityId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.AuditLogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(batchReference))
            query = query.Where(a => a.BatchReference == batchReference);

        if (!string.IsNullOrWhiteSpace(entityId))
            query = query.Where(a => a.EntityId == entityId);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip(Math.Max(0, (page - 1) * pageSize))
            .Take(Math.Max(1, pageSize))
            .ToListAsync(ct);

        return (items, total);
    }
}
