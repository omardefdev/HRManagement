using HR.Contracts.Application.Abstractions;
using HR.Contracts.Infrastructure.Context;
using HR.Contracts.Infrastructure.ExportJobs;
using HR.Contracts.Infrastructure.Messaging;
using HR.Contracts.Infrastructure.Repositories;
using HR.Contracts.Infrastructure.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Policy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Contracts.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHrContractsInfrastructure(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IExecutionContextAccessor, HrContractsExecutionContextAccessor>();
        services.AddScoped<IContractsRepository, ContractsRepository>();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        services.AddScoped<IContractsExportJobService, ContractsExportJobService>();

        if (configuration is not null)
        {
            services.AddHrPolicyEnforceClient(configuration);
            services.AddExportJobInfrastructure(configuration);
        }
        else
        {
            services.Configure<HRManagement.SharedKernel.Exports.ExportJobOptions>(_ => { });
            services.AddSingleton<IExportArtifactStore, LocalExportArtifactStore>();
        }

        services.AddEmployeeLifecycleGate(configuration);
        return services;
    }

    /// <summary>
    /// PLAN-37: the legal-hold / lifecycle gate against HR.Core (<c>GET /api/internal/employees/{id}/lifecycle-flags</c>).
    /// Authentication and fail-closed behaviour are PLAN-42; this registration only chooses the implementation.
    /// </summary>
    public static IServiceCollection AddEmployeeLifecycleGate(this IServiceCollection services, IConfiguration? configuration)
    {
        var hrCoreBase = configuration?["Services:HRCore:BaseUrl"] ?? configuration?["HrCore:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(hrCoreBase))
        {
            services.AddHttpClient(HttpEmployeeLifecycleGate.HttpClientName);
            services.AddScoped<IEmployeeLifecycleGate, HttpEmployeeLifecycleGate>();
        }
        else
        {
            services.AddSingleton<IEmployeeLifecycleGate, PassThroughEmployeeLifecycleGate>();
        }

        return services;
    }
}
