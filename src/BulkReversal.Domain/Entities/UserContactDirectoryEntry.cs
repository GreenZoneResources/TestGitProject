using BulkReversal.Domain.Common;

namespace BulkReversal.Domain.Entities;

/// <summary>
/// A read-only mirror of "as of this user's last SSO sign-in, they held this role and were
/// contactable at this email" — one row per (UserId, Role). Populated automatically by the SSO
/// authentication middleware from the JWT's "appRoles" claim on every successful sign-in; nothing
/// else writes to it, there is no API to grant or revoke an entry, and it is never consulted by
/// [Authorize]. Authorization is decided solely by the SSO JWT's role claims at request time — this
/// table exists only so the approval-routing notification emails (initiator -&gt; authorizer) know
/// who to reach without re-deriving the whole user directory from scratch on every batch submission.
/// </summary>
public class UserContactDirectoryEntry : AuditableEntity
{
    public string UserId { get; private set; } = string.Empty;
    public string UserName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;

    /// <summary>True if this role was present in the user's most recent SSO sign-in; set false
    /// (never deleted, so history/audit isn't lost) the next time that user signs in without it.</summary>
    public bool IsCurrentlyHeld { get; private set; } = true;

    public DateTimeOffset LastSeenAt { get; private set; } = DateTimeOffset.UtcNow;

    private UserContactDirectoryEntry() { }

    public static UserContactDirectoryEntry Create(string userId, string userName, string email, string role) => new()
    {
        UserId = userId,
        UserName = userName,
        Email = email,
        Role = role,
        IsCurrentlyHeld = true,
        LastSeenAt = DateTimeOffset.UtcNow,
        CreatedBy = "sso-sync"
    };

    public void RefreshFromSignIn(string userName, string email)
    {
        UserName = userName;
        Email = email;
        IsCurrentlyHeld = true;
        LastSeenAt = DateTimeOffset.UtcNow;
        Touch("sso-sync");
    }

    public void MarkNoLongerHeld()
    {
        IsCurrentlyHeld = false;
        Touch("sso-sync");
    }
}
