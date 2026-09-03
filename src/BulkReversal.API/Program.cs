using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using BulkReversal.API;
using BulkReversal.API.Middleware;
using BulkReversal.API.Swagger;
using BulkReversal.Application;
using BulkReversal.Infrastructure;
using BulkReversal.Infrastructure.Auth.ProviderApiKey;
using BulkReversal.Infrastructure.Auth.Sso;
using BulkReversal.Infrastructure.Persistence;
using HealthChecks.SqlServer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;

// Bootstrap logger: captures any failure that happens before the full Serilog pipeline (which
// reads its own configuration from appsettings) is built.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, loggerConfig) => loggerConfig
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "BulkReversal.API"));

    // --- Forwarded headers: correct scheme/host/PathBase when running behind IIS/nginx/a load
    // balancer, so absolute URLs (Location headers, Swagger's generated server URL) are right
    // post-deployment instead of reflecting the internal container/host address. ---
    var forwardedHeadersSection = builder.Configuration.GetSection("ForwardedHeaders");
    if (forwardedHeadersSection.GetValue("Enabled", true))
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            // Cleared (trust the immediate proxy regardless of address) only when explicitly
            // configured — typical for a reverse proxy sitting in front of the app on the same
            // trusted internal network. Leave populated in appsettings for tighter environments.
            var knownProxies = forwardedHeadersSection.GetSection("KnownProxies").Get<string[]>() ?? [];
            var knownNetworks = forwardedHeadersSection.GetSection("KnownNetworks").Get<string[]>() ?? [];

            if (knownProxies.Length == 0 && knownNetworks.Length == 0)
            {
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            }
            else
            {
                foreach (var proxy in knownProxies)
                {
                    if (System.Net.IPAddress.TryParse(proxy, out var ip)) options.KnownProxies.Add(ip);
                }
            }
        });
    }

    // --- MVC / JSON ---
    builder.Services
        .AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

    builder.Services.AddEndpointsApiExplorer();

    // --- API versioning (portal API). Provider endpoints are deliberately unversioned — see
    // ReversalsController. ---
    builder.Services
        .AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
        })
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

    // --- Swagger (Swashbuckle). Enabled based on config, not just Development — deployed
    // environments still get a working, browsable API contract unless explicitly turned off. ---
    builder.Services.ConfigureOptions<ConfigureSwaggerOptions>();
    builder.Services.AddSwaggerGen(options =>
    {
        options.OperationFilter<AuthorizeCheckOperationFilter>();

        options.AddSecurityDefinition(SsoAuthenticationExtensions.SchemeName, new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Settlement-portal SSO JWT. Normally delivered via the 'access-token' cookie by the " +
                          "Intranet gateway; paste a raw bearer token here to call the API directly from Swagger UI."
        });

        options.AddSecurityDefinition(ProviderApiKeyOptions.SchemeName, new OpenApiSecurityScheme
        {
            Name = "X-API-Key",
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Description = "API key issued to SingleReversalEngine.Orchestrator for the provider-contract endpoints."
        });

        foreach (var xmlFile in Directory.GetFiles(AppContext.BaseDirectory, "BulkReversal.*.xml"))
        {
            options.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
        }
    });

    // --- Application / Infrastructure wiring ---
    builder.Services.AddApplicationServices(builder.Configuration);
    builder.Services.AddInfrastructureServices(builder.Configuration);

    // --- Rate limiting for the reversal-engine-facing endpoints ---
    var providerRateLimit = builder.Configuration.GetSection("RateLimiting:Provider");
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddFixedWindowLimiter(RateLimiterPolicies.Provider, limiterOptions =>
        {
            limiterOptions.PermitLimit = providerRateLimit.GetValue("PermitLimit", 120);
            limiterOptions.Window = TimeSpan.FromSeconds(providerRateLimit.GetValue("WindowSeconds", 60));
            limiterOptions.QueueLimit = providerRateLimit.GetValue("QueueLimit", 0);
            limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        });
    });

    // --- Health checks ---
    var healthChecksBuilder = builder.Services.AddHealthChecks();
    var dbConnectionString = builder.Configuration.GetConnectionString("BulkReversalConnection");
    if (!string.IsNullOrWhiteSpace(dbConnectionString))
    {
        healthChecksBuilder.AddSqlServer(dbConnectionString, name: "sql-server", tags: ["ready"]);
    }

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("IntranetPortal", policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }
        });
    });

    var app = builder.Build();

    if (forwardedHeadersSection.GetValue("Enabled", true))
    {
        app.UseForwardedHeaders();
    }

    app.UseSerilogRequestLogging();

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    var swaggerEnabled = app.Configuration.GetValue("Swagger:Enabled", true);
    if (swaggerEnabled)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            var versionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
            foreach (var description in versionProvider.ApiVersionDescriptions)
            {
                options.SwaggerEndpoint($"{description.GroupName}/swagger.json", $"Settlement Portal API {description.GroupName}");
            }
            options.SwaggerEndpoint("provider/swagger.json", "Reversal Engine Provider Contract");
            options.RoutePrefix = "swagger";
            options.DisplayRequestDuration();
        });
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseCors("IntranetPortal");

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseRateLimiter();

    app.MapControllers();
    app.MapHealthChecks("/health");

    // Apply pending EF Core migrations automatically on startup when configured — convenient for
    // containerized deployments; disable for environments where DBAs run migrations out-of-band.
    if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BulkReversalDbContext>();
        db.Database.Migrate();
    }

    Log.Information("BulkReversal.API starting up in {Environment} environment.", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "BulkReversal.API terminated unexpectedly during startup.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Exposed for WebApplicationFactory-based integration testing.</summary>
public partial class Program;
