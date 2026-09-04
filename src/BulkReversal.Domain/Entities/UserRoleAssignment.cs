using BulkReversal.Domain.Common;
using BulkReversal.Domain.Exceptions;

namespace BulkReversal.Domain.Entities;

/// <summary>
/// Grants one BulkReversal application role (see the Application layer's AppRoles constants) to a
/// user, in addition to whatever the Bank's SSO "appRoles" claim already carries for this
/// application (see SsoAuthenticationExtensions in the Infrastructure layer, which merges the two
/// at token-validation time). Administrator-managed, so access can be granted or revoked
/// immediately without waiting on a central AD/SSO update — and so the approval workflow has a
/// directory of who to notify (Email) when routing a batch from initiator to authorizer.
/// </summary>
public class UserRoleAssignment : AuditableEntity
{
    public string UserId { get; private set; } = string.Empty;
    public string UserName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public string AssignedByUserId { get; private set; } = string.Empty;
    public string AssignedByName { get; private set; } = string.Empty;

    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedByUserId { get; private set; }
    public string? RevokedByName { get; private set; }

    private UserRoleAssignment() { }

    public static UserRoleAssignment Create(
        string userId, string userName, string email, string role, string assignedByUserId, string assignedByName)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("User ID is required.");
        if (string.IsNullOrWhiteSpace(userName))
            throw new DomainException("User name is required.");
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email is required.");
        if (string.IsNullOrWhiteSpace(role))
            throw new DomainException("Role is required.");

        return new UserRoleAssignment
        {
            UserId = userId.Trim(),
            UserName = userName.Trim(),
            Email = email.Trim(),
            Role = role.Trim(),
            IsActive = true,
            AssignedByUserId = assignedByUserId,
            AssignedByName = assignedByName,
            CreatedBy = assignedByUserId
        };
    }

    public void Revoke(string revokedByUserId, string revokedByName)
    {
        if (!IsActive)
            throw new DomainException($"Role '{Role}' for user '{UserId}' has already been revoked.");

        IsActive = false;
        RevokedAt = DateTimeOffset.UtcNow;
        RevokedByUserId = revokedByUserId;
        RevokedByName = revokedByName;
        Touch(revokedByUserId);
    }
}
