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
                    // Resolves the caller's effective BulkReversal roles from two sources and
                    // validates that at least one of them granted access: the SSO "appRoles" claim
                    // (the Bank's central AD/SSO app-role registry) and this application's own
                    // locally-assigned role table (see UserRoleAssignment / IRoleAssignmentService),
                    // managed by a BulkReversal Administrator so access can be granted or revoked
                    // immediately without waiting on a central AD/SSO update. Neither source alone
                    // is trusted as final — the merged, de-duplicated set is what actually drives
                    // [Authorize(Roles = ...)] below.
                    OnTokenValidated = async context =>
                    {
                        var identity = context.Principal?.Identity as ClaimsIdentity;
                        var userId = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? identity?.FindFirst("sub")?.Value;

                        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        var appRolesClaim = identity?.FindFirst(ssoOptions.AppRolesClaimName)?.Value;
                        if (!string.IsNullOrWhiteSpace(appRolesClaim))
                        {
                            try
                            {
                                foreach (var role in ExtractRolesFromAppRolesMap(appRolesClaim))
                                {
                                    roles.Add(role);
                                }
                            }
                            catch (Exception ex)
                            {
                                context.Fail($"Role extraction failed: {ex.Message}");
                                return;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(userId))
                        {
                            try
                            {
                                var roleRepository = context.HttpContext.RequestServices.GetRequiredService<IUserRoleAssignmentRepository>();
                                var assignedRoles = await roleRepository.GetActiveRoleNamesAsync(userId, context.HttpContext.RequestAborted);
                                foreach (var role in assignedRoles)
                                {
                                    roles.Add(role);
                                }
                            }
                            catch (Exception ex)
                            {
                                // A transient DB hiccup here must not lock out every user who already
                                // has valid SSO app-roles — fail open on THIS source only, and let the
                                // zero-roles check below still deny access if that was the sole grant.
                                var logger = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?
                                    .CreateLogger("SsoAuthentication");
                                logger?.LogWarning(ex, "Could not resolve locally-assigned roles for {UserId}; continuing with SSO app-roles only.", userId);
                            }
                        }

                        if (roles.Count == 0)
                        {
                            context.Fail("No application roles could be resolved for this user from either the SSO app-role map or local role assignment.");
                            return;
                        }

                        foreach (var role in roles)
                        {
                            identity?.AddClaim(new Claim(ClaimTypes.Role, role));
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
