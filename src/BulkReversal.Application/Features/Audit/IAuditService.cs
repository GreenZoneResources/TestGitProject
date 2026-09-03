using BulkReversal.Domain.Enums;

namespace BulkReversal.Application.Features.Audit;

/// <summary>Writes timestamped, user-attributed audit trail entries (FR-18). Does not save changes
/// itself — entries are attached to the current unit of work and persisted with it.</summary>
public interface IAuditService
{
    void Record(AuditAction action, string entityType, string entityId, string? batchReference, string details);
}
