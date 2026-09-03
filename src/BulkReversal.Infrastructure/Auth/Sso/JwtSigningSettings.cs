namespace BulkReversal.Infrastructure.Auth.Sso;

/// <summary>JWT validation parameters sourced either from the SSO database (production) or the
/// appsettings fallback (local/dev) — see <see cref="SsoOptions"/>.</summary>
public record JwtSigningSettings(string Issuer, string Audience, string Key);
