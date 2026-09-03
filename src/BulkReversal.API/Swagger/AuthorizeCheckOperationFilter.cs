using BulkReversal.Infrastructure.Auth.ProviderApiKey;
using BulkReversal.Infrastructure.Auth.Sso;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BulkReversal.API.Swagger;

/// <summary>Attaches the correct security requirement (SSO bearer vs. provider API key) to each
/// operation, so Swagger UI's "Authorize" flow and the 401 responses it documents match reality.</summary>
public class AuthorizeCheckOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasAuthorize = context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any() == true
            || context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any();

        if (!hasAuthorize) return;

        var authorizeAttributes = context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<AuthorizeAttribute>()
            .Concat(context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>()) ?? [];

        var usesProviderScheme = authorizeAttributes.Any(a =>
            a.AuthenticationSchemes?.Contains(ProviderApiKeyOptions.SchemeName) == true);

        var schemeId = usesProviderScheme ? ProviderApiKeyOptions.SchemeName : SsoAuthenticationExtensions.SchemeName;

        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized" });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Forbidden" });

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = schemeId } }] = []
        });
    }
}
