using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkReversal.Infrastructure.Auth.ProviderApiKey;

/// <summary>Validates an "Authorization: ApiKey &lt;key&gt;" header against the configured provider key(s) using a
/// constant-time comparison, to avoid leaking key material through response-timing side channels.</summary>
public class ProviderApiKeyAuthenticationHandler : AuthenticationHandler<ProviderApiKeyOptions>
{
    public const string ProviderIdentityName = "reversal-engine";

    public ProviderApiKeyAuthenticationHandler(
        IOptionsMonitor<ProviderApiKeyOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues) || headerValues.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"Missing 'Authorization' header. Expected: Authorization: {Options.AuthorizationScheme} <key>"));
        }

        var headerValue = headerValues.ToString();
        var expectedPrefix = Options.AuthorizationScheme + " ";
        if (!headerValue.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"Malformed 'Authorization' header. Expected: Authorization: {Options.AuthorizationScheme} <key>"));
        }

        var provided = headerValue[expectedPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(provided))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"Malformed 'Authorization' header. Expected: Authorization: {Options.AuthorizationScheme} <key>"));
        }

        if (Options.ApiKeys.Length == 0)
        {
            Logger.LogError("ProviderApiKey:ApiKeys is not configured; rejecting all provider requests.");
            return Task.FromResult(AuthenticateResult.Fail("Provider authentication is not configured."));
        }

        if (!Options.ApiKeys.Any(configured => FixedTimeEquals(provided, configured)))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, ProviderIdentityName) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);

        // Lengths necessarily differ for most mismatches, but comparing against a hash keeps the
        // overall check length-independent rather than short-circuiting on Length !=.
        var hashA = SHA256.HashData(bytesA);
        var hashB = SHA256.HashData(bytesB);
        return CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }
}
