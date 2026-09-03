using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BulkReversal.API.Swagger;

/// <summary>
/// Publishes one Swagger document per discovered API version ("v1", "v2", ...) plus a separate
/// "provider" document for the two SingleReversalEngine.Orchestrator-facing endpoints, which are
/// intentionally unversioned. Runs after Asp.Versioning has discovered the app's API versions, so
/// new versions get their own document automatically without touching this file.
/// </summary>
public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _versionProvider;

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider versionProvider) => _versionProvider = versionProvider;

    public void Configure(SwaggerGenOptions options)
    {
        // The options factory can invoke IConfigureOptions<SwaggerGenOptions> more than once while
        // building a single SwaggerGenOptions instance (e.g. once per named/unnamed resolution), so
        // this assigns by key rather than using the Add-only SwaggerDoc() extension, keeping the
        // method idempotent instead of throwing on a re-run.
        var docs = options.SwaggerGeneratorOptions.SwaggerDocs;

        foreach (var description in _versionProvider.ApiVersionDescriptions)
        {
            docs[description.GroupName] = new OpenApiInfo
            {
                Title = "BulkReversal.API — Settlement Portal",
                Version = description.ApiVersion.ToString(),
                Description =
                    "Failed Transaction Reversal Portal API: batch upload, validation, approvals, " +
                    "and status monitoring for the Settlement Team." +
                    (description.IsDeprecated ? " This API version has been deprecated." : string.Empty)
            };
        }

        docs["provider"] = new OpenApiInfo
        {
            Title = "BulkReversal.API — Reversal Engine Provider Contract",
            Version = "provider",
            Description =
                "The two endpoints SingleReversalEngine.Orchestrator calls against this application " +
                "as a \"provider\": GET pending reversals and POST the outcome callback. Secured with " +
                "an API key (X-API-Key), independent of the Settlement-portal SSO login."
        };

        options.DocInclusionPredicate((docName, apiDesc) => apiDesc.GroupName == docName);
    }
}
