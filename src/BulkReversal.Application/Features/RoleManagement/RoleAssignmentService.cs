using BulkReversal.Application.Common.Exceptions;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Features.Audit;
using BulkReversal.Application.Features.RoleManagement.Dtos;
using BulkReversal.Domain.Entities;
using BulkReversal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BulkReversal.Application.Features.RoleManagement;

/// <summary>
/// Administrator-facing role assignment, backing the API layer's role-management endpoints.
/// Locally-assigned roles are merged into a caller's claims by the SSO authentication middleware
/// alongside whatever the SSO "appRoles" claim carries.
/// </summary>
public class RoleAssignmentService : IRoleAssignmentService
{
    private readonly IUserRoleAssignmentRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly ILogger<RoleAssignmentService> _logger;

    public RoleAssignmentService(
        IUserRoleAssignmentRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ILogger<RoleAssignmentService> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RoleAssignmentDto>> ListAsync(CancellationToken ct = default)
    {
        var assignments = await _repository.ListAsync(ct);
        return assignments.Select(MapToDto).ToList();
    }

    public async Task<RoleAssignmentDto> AssignRoleAsync(AssignRoleRequest request, CancellationToken ct = default)
    {
        if (await _repository.HasActiveAssignmentAsync(request.UserId, request.Role, ct))
        {
            throw new ConflictAppException(
                $"User '{request.UserId}' already has an active '{request.Role}' assignment.");
        }

        var assignment = UserRoleAssignment.Create(
            request.UserId, request.UserName, request.Email, request.Role, _currentUser.UserId, _currentUser.UserName);

        _repository.Add(assignment);

        _auditService.Record(
            AuditAction.RoleAssigned,
            nameof(UserRoleAssignment),
            assignment.Id.ToString(),
            batchReference: null,
            $"Role '{assignment.Role}' assigned to {assignment.UserName} ({assignment.UserId}) by {_currentUser.UserName}.");

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Role {Role} assigned to {UserId} by {AssignedBy}.", assignment.Role, assignment.UserId, _currentUser.UserId);

        return MapToDto(assignment);
    }

    public async Task RevokeRoleAsync(Guid id, CancellationToken ct = default)
    {
        var assignment = await _repository.GetByIdAsync(id, ct)
            ?? throw new NotFoundAppException(nameof(UserRoleAssignment), id);

        assignment.Revoke(_currentUser.UserId, _currentUser.UserName);

        _auditService.Record(
            AuditAction.RoleRevoked,
            nameof(UserRoleAssignment),
            assignment.Id.ToString(),
            batchReference: null,
            $"Role '{assignment.Role}' revoked from {assignment.UserName} ({assignment.UserId}) by {_currentUser.UserName}.");

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Role {Role} revoked from {UserId} by {RevokedBy}.", assignment.Role, assignment.UserId, _currentUser.UserId);
    }

    private static RoleAssignmentDto MapToDto(UserRoleAssignment a) => new(
        a.Id, a.UserId, a.UserName, a.Email, a.Role, a.IsActive, a.CreatedAt, a.AssignedByUserId, a.AssignedByName, a.RevokedAt);
}
