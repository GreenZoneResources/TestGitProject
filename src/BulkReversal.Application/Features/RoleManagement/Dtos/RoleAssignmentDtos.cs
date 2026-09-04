namespace BulkReversal.Application.Features.RoleManagement.Dtos;

/// <summary>Grants <paramref name="Role"/> (one of <see cref="Common.Constants.AppRoles.Assignable"/>)
/// to the named user. <paramref name="UserId"/> must match the identifier the SSO JWT presents for
/// that user (the "sub"/NameIdentifier claim) so the role takes effect on their next sign-in.</summary>
public record AssignRoleRequest(string UserId, string UserName, string Email, string Role);

public record RoleAssignmentDto(
    Guid Id,
    string UserId,
    string UserName,
    string Email,
    string Role,
    bool IsActive,
    DateTimeOffset AssignedAt,
    string AssignedByUserId,
    string AssignedByName,
    DateTimeOffset? RevokedAt);
