using BulkReversal.Domain.Entities;

namespace BulkReversal.Application.Common.Interfaces.Persistence;

public interface IUserRoleAssignmentRepository
{
    Task<UserRoleAssignment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<bool> HasActiveAssignmentAsync(string userId, string role, CancellationToken ct = default);

    /// <summary>All active role names locally assigned to this user — merged into the caller's
    /// claims by the SSO authentication middleware alongside whatever the SSO "appRoles" claim
    /// carries (see <c>SsoAuthenticationExtensions.OnTokenValidated</c>).</summary>
    Task<IReadOnlyList<string>> GetActiveRoleNamesAsync(string userId, CancellationToken ct = default);

    /// <summary>Distinct email addresses of every user with an active assignment to any of
    /// <paramref name="roles"/> — used to route the "submitted for approval" notification from an
    /// initiator to every current authorizer.</summary>
    Task<IReadOnlyList<string>> GetActiveEmailsByRolesAsync(IEnumerable<string> roles, CancellationToken ct = default);

    /// <summary>The most recently assigned active email on file for this user, if any — used to
    /// notify an initiator of an approve/reject decision.</summary>
    Task<string?> GetActiveEmailForUserAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<UserRoleAssignment>> ListAsync(CancellationToken ct = default);

    void Add(UserRoleAssignment assignment);
}
