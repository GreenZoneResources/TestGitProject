namespace BulkReversal.Infrastructure.Auth.Sso;

/// <summary>
/// Configures the Bank's SSO integration (bound from appsettings.json "Sso"). Settlement users
/// authenticate once against the Bank's existing Intranet/AD-backed SSO (FR-01) — this app only
/// validates the JWT it already holds, it never issues or refreshes tokens itself.
/// </summary>
public class SsoOptions
{
    public const string SectionName = "Sso";

    /// <summary>Name of the cookie the SSO gateway sets, carrying the bearer JWT (matches the
    /// existing Intranet portal's cookie so no separate login is required, FR-01).</summary>
    public string AccessTokenCookieName { get; set; } = "access-token";

    /// <summary>Claim name carrying the caller's per-application role map (JSON), from which
    /// standard ClaimTypes.Role claims are derived.</summary>
    public string AppRolesClaimName { get; set; } = "appRoles";

    /// <summary>When true (default, production), JWT signing settings (Issuer/Audience/Key) are
    /// loaded from the SSO database via [dbo].[GetTokenSettings], exactly as the Bank's shared SSO
    /// integration pattern requires. When false, the <see cref="Jwt"/> fallback section below is
    /// used instead — intended for local development/testing where the SSO database is unreachable.</summary>
    public bool UseDatabaseTokenSettings { get; set; } = true;

    /// <summary>Named connection string (in ConnectionStrings) pointing at the SSO database that
    /// hosts [dbo].[GetTokenSettings]. Only used when <see cref="UseDatabaseTokenSettings"/> is true.</summary>
    public string ConnectionStringName { get; set; } = "SingleSignOnConnection";

    /// <summary>Fallback static JWT settings, used only when <see cref="UseDatabaseTokenSettings"/> is false.</summary>
    public JwtFallbackOptions Jwt { get; set; } = new();

    public class JwtFallbackOptions
    {
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;

        /// <summary>Base64-encoded symmetric signing key.</summary>
        public string Key { get; set; } = string.Empty;
    }
}
