using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using BulkReversal.Application.Common.Interfaces.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace BulkReversal.Infrastructure.Auth.Sso;

/// <summary>
/// Wires the "CustomJwt" authentication scheme against the Bank's existing SSO (FR-01/FR-02):
/// the JWT is presented via the "access-token" cookie set by the Intranet gateway, signing
/// settings are sourced from the SSO database (or appsettings fallback, see
/// <see cref="SsoOptions"/>), and each caller's per-application role map ("appRoles" claim) is
/// expanded into standard <see cref="ClaimTypes.Role"/> claims so [Authorize(Roles = ...)] works.
/// This is a faithful port of the Bank's shared ServiceManager.RegisterAuthService pattern.
/// </summary>
public static class SsoAuthenticationExtensions
{
    public const string SchemeName = "CustomJwt";
    public const string PolicyName = "CustomJwtPolicy";

    public static IServiceCollection AddSsoAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SsoOptions>()
            .Bind(configuration.GetSection(SsoOptions.SectionName))
            .ValidateOnStart();

        services.AddScoped<IJwtSigningSettingsProvider, JwtSigningSettingsProvider>();

        var ssoOptions = configuration.GetSection(SsoOptions.SectionName).Get<SsoOptions>() ?? new SsoOptions();

        // JWT signing settings must be known before AddJwtBearer's options can be built, and that
        // happens during service registration (before the DI container exists) — so, exactly like
        // the Bank's shared ServiceManager, we resolve them once here, synchronously, at startup.
        var bootstrapProvider = new JwtSigningSettingsProvider(
            Microsoft.Extensions.Options.Options.Create(ssoOptions), configuration, NullLogger<JwtSigningSettingsProvider>.Instance);

        JwtSigningSettings settings;
        try
        {
            settings = bootstrapProvider.GetAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "SSO token settings could not be loaded at startup. Check SSO database connectivity/configuration, " +
                "or set Sso:UseDatabaseTokenSettings=false with a Sso:Jwt fallback for local development.", ex);
        }

        byte[] signingKeyBytes;
        try
        {
            signingKeyBytes = Convert.FromBase64String(settings.Key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Invalid Base64 format for the JWT signing key.", ex);
        }

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SchemeName;
                options.DefaultChallengeScheme = SchemeName;
            })
            .AddJwtBearer(SchemeName, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
                    RoleClaimType = ClaimTypes.Role,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Cookies[ssoOptions.AccessTokenCookieName];
                        if (!string.IsNullOrEmpty(token))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                    // Authorization is decided solely by the SSO "appRoles" claim (the Bank's
                    // central AD/SSO app-role registry for this application, FR-01/FR-02) — there is
                    // no locally-managed role/permission override. A best-effort, non-authoritative
                    // sync of the resolved roles into UserContactDirectory happens afterward purely
                    // so the approval-routing notification emails know who to reach; it never affects
                    // this authentication/authorization decision.
                    OnTokenValidated = async context =>
                    {
                        var identity = context.Principal?.Identity as ClaimsIdentity;
                        var appRolesClaim = identity?.FindFirst(ssoOptions.AppRolesClaimName)?.Value;

                        if (string.IsNullOrWhiteSpace(appRolesClaim))
                        {
                            context.Fail("Application roles not found.");
                            return;
                        }

                        List<string> roles;
                        try
                        {
                            roles = ExtractRolesFromAppRolesMap(appRolesClaim);
                        }
                        catch (Exception ex)
                        {
                            context.Fail($"Role extraction failed: {ex.Message}");
                            return;
                        }

                        if (roles.Count == 0)
                        {
                            context.Fail("Application roles not found.");
                            return;
                        }

                        foreach (var role in roles)
                        {
                            identity?.AddClaim(new Claim(ClaimTypes.Role, role));
                        }

                        var userId = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? identity?.FindFirst("sub")?.Value;
                        var userName = identity?.FindFirst("name")?.Value ?? identity?.FindFirst(ClaimTypes.Name)?.Value ?? userId;
                        var email = identity?.FindFirst(ClaimTypes.Email)?.Value ?? identity?.FindFirst("email")?.Value;

                        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(email))
                        {
                            try
                            {
                                var directory = context.HttpContext.RequestServices.GetRequiredService<IUserContactDirectoryRepository>();
                                await directory.SyncFromSignInAsync(userId, userName ?? userId, email, roles, context.HttpContext.RequestAborted);
                            }
                            catch (Exception ex)
                            {
                                var logger = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?
                                    .CreateLogger("SsoAuthentication");
                                logger?.LogWarning(ex, "Could not sync the notification contact directory for {UserId}; sign-in still succeeds.", userId);
                            }
                        }
                    },
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?
                            .CreateLogger("SsoAuthentication");
                        logger?.LogWarning(context.Exception, "JWT authentication failed.");
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                policy.AuthenticationSchemes.Add(SchemeName);
                policy.RequireAuthenticatedUser();
            });
        });

        return services;
    }

    private static List<string> ExtractRolesFromAppRolesMap(string json)
    {
        try
        {
            var entries = JsonSerializer.Deserialize<List<GroupedRoleDto>>(json, JsonOptions);
            return entries?.SelectMany(e => e.Roles).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private class GroupedRoleDto
    {
        [JsonPropertyName("applicationName")]
        public string ApplicationName { get; set; } = string.Empty;

        [JsonPropertyName("roles")]
        public string[] Roles { get; set; } = [];

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
