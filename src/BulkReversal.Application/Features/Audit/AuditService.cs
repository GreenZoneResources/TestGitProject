using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;

namespace BulkReversal.Application.Features.Audit;

public class AuditService : IAuditService
{
    private readonly IAuditLogRepository _repository;
    private readonly ICurrentUserService _currentUser;

    public AuditService(IAuditLogRepository repository, ICurrentUserService currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public void Record(AuditAction action, string entityType, string entityId, string? batchReference, string details)
    {
        var entry = AuditLogEntry.Create(
            action,
            entityType,
            entityId,
            batchReference,
            _currentUser.UserId,
            _currentUser.UserName,
            _currentUser.IpAddress,
            details);

        _repository.Add(entry);
    }
}
