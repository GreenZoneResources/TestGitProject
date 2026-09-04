using BulkReversal.Application.Features.RoleManagement.Dtos;

namespace BulkReversal.Application.Features.RoleManagement;

public interface IRoleAssignmentService
{
    Task<IReadOnlyList<RoleAssignmentDto>> ListAsync(CancellationToken ct = default);

    Task<RoleAssignmentDto> AssignRoleAsync(AssignRoleRequest request, CancellationToken ct = default);

    Task RevokeRoleAsync(Guid id, CancellationToken ct = default);
}
