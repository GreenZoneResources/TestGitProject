using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Interfaces.Persistence;
using BulkReversal.Application.Common.Options;
using BulkReversal.Infrastructure.Auth;
using BulkReversal.Infrastructure.Auth.ProviderApiKey;
using BulkReversal.Infrastructure.Auth.Sso;
using BulkReversal.Infrastructure.Email;
using BulkReversal.Infrastructure.ExternalServices;
using BulkReversal.Infrastructure.Persistence;
using BulkReversal.Infrastructure.Persistence.Repositories;
using BulkReversal.Infrastructure.Reporting;
using BulkReversal.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BulkReversal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BulkReversalDbContext>(options =>
        {
            // "Database:Provider" defaults to SQL Server (the only supported provider in a real
            // deployment); "InMemory" exists solely so the app and its request pipeline can run
            // without a live SQL Server for local exploration/smoke-testing — never set it in a
            // deployed environment (no migrations, no durability, no concurrency guarantees).
            var provider = configuration.GetValue("Database:Provider", "SqlServer");

            if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
            {
                options.UseInMemoryDatabase("BulkReversalDb");
                return;
            }

            var connectionString = configuration.GetConnectionString("BulkReversalConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:BulkReversalConnection is not configured.");
            }

            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.MigrationsAssembly(typeof(BulkReversalDbContext).Assembly.FullName);
            });
        });

        services.AddScoped<IReversalBatchRepository, ReversalBatchRepository>();
        services.AddScoped<IReversalTransactionRepository, ReversalTransactionRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IUserContactDirectoryRepository, UserContactDirectoryRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IBatchReferenceGenerator, BatchReferenceGenerator>();

        services.AddScoped<IReportExportService, ReportExportService>();
        services.AddScoped<IEmailService, SmtpEmailService>();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        // BRU-05: independent confirmation that a submitted reference matches a genuine source
        // transaction. Disabled by default (see TransferServiceOptions); the typed HttpClient never
        // has its BaseAddress set because TransferServiceClient builds the full absolute URL itself
        // (BaseUrl + path), matching the Transfer Service's own convention.
        services.AddOptions<TransferServiceOptions>()
            .Bind(configuration.GetSection(TransferServiceOptions.SectionName))
            .ValidateOnStart();
        services.AddHttpClient<ITransferService, TransferServiceClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TransferServiceOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 60));
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Portal (Settlement Team) authentication: SSO-backed JWT, cookie-delivered (FR-01/FR-02).
        services.AddSsoAuthentication(configuration);

        // Reversal-engine-facing authentication: static API key on the two provider endpoints
        // (provider-integration-contract.md §3). AddScheme resolves options *named* after the
        // scheme (via IOptionsMonitor<T>.Get(schemeName)), so the appsettings binding must target
        // that same name — the default/unnamed Configure<T>() overload would silently miss it.
        services.Configure<ProviderApiKeyOptions>(
            ProviderApiKeyOptions.SchemeName, configuration.GetSection(ProviderApiKeyOptions.SectionName));
        services.AddAuthentication().AddScheme<ProviderApiKeyOptions, ProviderApiKeyAuthenticationHandler>(
            ProviderApiKeyOptions.SchemeName, _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ProviderApiKeyOptions.SchemeName, policy =>
            {
                policy.AuthenticationSchemes.Add(ProviderApiKeyOptions.SchemeName);
                policy.RequireAuthenticatedUser();
            });
        });

        return services;
    }
}
