using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkReversal.Infrastructure.Auth.Sso;

/// <summary>
/// Loads JWT signing settings for the "CustomJwt" scheme. Faithful port of the Bank's shared
/// ServiceManager.GetTokenSettingsAsync (calls [dbo].[GetTokenSettings] on the SSO database),
/// with an appsettings-driven fallback for environments without SSO database connectivity.
/// </summary>
public interface IJwtSigningSettingsProvider
{
    Task<JwtSigningSettings> GetAsync(CancellationToken ct = default);
}

public class JwtSigningSettingsProvider : IJwtSigningSettingsProvider
{
    private readonly SsoOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JwtSigningSettingsProvider> _logger;

    public JwtSigningSettingsProvider(IOptions<SsoOptions> options, IConfiguration configuration, ILogger<JwtSigningSettingsProvider> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<JwtSigningSettings> GetAsync(CancellationToken ct = default)
    {
        if (!_options.UseDatabaseTokenSettings)
        {
            var fallback = _options.Jwt;
            if (string.IsNullOrWhiteSpace(fallback.Issuer) || string.IsNullOrWhiteSpace(fallback.Audience) || string.IsNullOrWhiteSpace(fallback.Key))
            {
                throw new InvalidOperationException(
                    $"Sso:UseDatabaseTokenSettings is false but Sso:Jwt (Issuer/Audience/Key) is not fully configured in appsettings.");
            }

            _logger.LogWarning("SSO token settings loaded from appsettings fallback (Sso:Jwt) — use only for local development.");
            return new JwtSigningSettings(fallback.Issuer, fallback.Audience, fallback.Key);
        }

        var connectionString = _configuration.GetConnectionString(_options.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{_options.ConnectionStringName}' is not configured.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand("[dbo].[GetTokenSettings]", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);

        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException("GetTokenSettings returned no rows; SSO token settings are not configured.");
        }

        var audience = GetNullableString(reader, "Audience");
        var issuer = GetNullableString(reader, "Issuer");
        var key = GetNullableString(reader, "Key");

        if (string.IsNullOrWhiteSpace(audience) || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("GetTokenSettings returned incomplete JWT configuration.");
        }

        return new JwtSigningSettings(issuer, audience, key);
    }

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
    }
}
