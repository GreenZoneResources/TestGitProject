using System.Reflection;
using BulkReversal.Application.Common.Options;
using BulkReversal.Application.Features.Approvals;
using BulkReversal.Application.Features.Audit;
using BulkReversal.Application.Features.Provider;
using BulkReversal.Application.Features.RoleManagement;
using BulkReversal.Application.Features.StatusMonitoring;
using BulkReversal.Application.Features.Upload.Services;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BulkReversal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BusinessRulesOptions>()
            .Bind(configuration.GetSection(BusinessRulesOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ProviderIntegrationOptions>()
            .Bind(configuration.GetSection(ProviderIntegrationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();

        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        services.AddScoped<UploadRowFieldValidator>();
        services.AddScoped<IBatchUploadService, BatchUploadService>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<IStatusMonitoringService, StatusMonitoringService>();
        services.AddScoped<IProviderIntegrationService, ProviderIntegrationService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IRoleAssignmentService, RoleAssignmentService>();

        return services;
    }
}
