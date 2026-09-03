using BulkReversal.Domain.Entities;

namespace BulkReversal.Application.Common.Interfaces.Persistence;

public interface IAuditLogRepository
{
    void Add(AuditLogEntry entry);

    Task<(IReadOnlyList<AuditLogEntry> Items, int TotalCount)> SearchAsync(
        string? batchReference,
        string? entityId,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
