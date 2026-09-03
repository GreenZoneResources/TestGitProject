namespace BulkReversal.Application.Common.Interfaces;

/// <summary>
/// Abstracts the authenticated caller so Application services never depend on
/// ClaimsPrincipal/ASP.NET Core directly. Populated from the SSO JWT ("CustomJwt" scheme) claims.
/// </summary>
public interface ICurrentUserService
{
    bool IsAuthenticated { get; }
    string UserId { get; }
    string UserName { get; }
    string? Email { get; }
    IReadOnlyCollection<string> Roles { get; }
    string? IpAddress { get; }

    bool IsInRole(string role);
}
