using BulkReversal.Domain.Enums;

namespace BulkReversal.Domain.Entities;

/// <summary>
/// Immutable, timestamped record of a reversal-lifecycle action (FR-18): upload, validation,
/// submission, API retrieval, processing outcome, or exception.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; private set; }
    public AuditAction Action { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string? BatchReference { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string UserName { get; private set; } = string.Empty;
    public string? IpAddress { get; private set; }
    public string Details { get; private set; } = string.Empty;
    public DateTimeOffset Timestamp { get; private set; } = DateTimeOffset.UtcNow;

    private AuditLogEntry() { }

    public static AuditLogEntry Create(
        AuditAction action,
        string entityType,
        string entityId,
        string? batchReference,
        string userId,
        string userName,
        string? ipAddress,
        string details)
    {
        return new AuditLogEntry
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BatchReference = batchReference,
            UserId = string.IsNullOrWhiteSpace(userId) ? "system" : userId,
            UserName = string.IsNullOrWhiteSpace(userName) ? "system" : userName,
            IpAddress = ipAddress,
            Details = details,
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}
