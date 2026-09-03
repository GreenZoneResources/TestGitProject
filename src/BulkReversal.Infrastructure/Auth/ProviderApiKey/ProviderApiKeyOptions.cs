using Microsoft.AspNetCore.Authentication;

namespace BulkReversal.Infrastructure.Auth.ProviderApiKey;

/// <summary>
/// Secures the two machine-to-machine endpoints the reversal engine calls (GET pending-reversals,
/// POST callback — provider-integration-contract.md §3, AuthType "ApiKey": header
/// "X-API-Key: &lt;secret&gt;"). Bound from appsettings.json ("ProviderApiKey"). These endpoints are
/// never used by Settlement-team browsers, so they intentionally sit outside the SSO/"CustomJwt"
/// scheme used by the portal UI.
/// </summary>
public class ProviderApiKeyOptions : AuthenticationSchemeOptions
{
    public const string SectionName = "ProviderApiKey";
    public const string SchemeName = "ProviderApiKey";

    public string HeaderName { get; set; } = "X-API-Key";

    /// <summary>
    /// One or more accepted keys (supports zero-downtime rotation: add the new key, redeploy Wisdom's
    /// config, then remove the old one). Prefer supplying these via environment variables or a secret
    /// store rather than committing raw values into appsettings.json — ASP.NET Core configuration
    /// automatically layers environment variables over appsettings, e.g.
    /// ProviderApiKey__ApiKeys__0=&lt;secret&gt;.
    /// </summary>
    public string[] ApiKeys { get; set; } = [];
}
