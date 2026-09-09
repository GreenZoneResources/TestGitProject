namespace BulkReversal.Application.Common.Interfaces.Persistence;

/// <summary>
/// A read-only-to-the-outside mirror of SSO role/email information, kept in sync solely by the SSO
/// authentication middleware on every sign-in (see UserContactDirectoryEntry). Used only to route
/// approval-workflow notification emails — never consulted for authorization decisions, which are
/// decided purely from the SSO JWT's role claims at request time.
/// </summary>
public interface IUserContactDirectoryRepository
{
    /// <summary>Mirrors the given user's latest SSO sign-in: upserts an entry for every role in
    /// <paramref name="roles"/>, and marks any of that user's previously-recorded roles not in this
    /// set as no-longer-held. Self-contained (saves its own changes) and best-effort — callers
    /// should treat a failure here as non-fatal to authentication.</summary>
    Task SyncFromSignInAsync(string userId, string userName, string email, IReadOnlyCollection<string> roles, CancellationToken ct = default);

    /// <summary>Distinct emails of every user whose most recent SSO sign-in held any of <paramref name="roles"/>.</summary>
    Task<IReadOnlyList<string>> GetEmailsByRolesAsync(IEnumerable<string> roles, CancellationToken ct = default);

    /// <summary>The email on file for this user as of their most recent SSO sign-in, if any.</summary>
    Task<string?> GetEmailForUserAsync(string userId, CancellationToken ct = default);
}
