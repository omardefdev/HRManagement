using HR.Contracts.Application.Abstractions;
using HR.Contracts.Infrastructure.Context;
using HR.Contracts.Infrastructure.ExportJobs;
using HR.Contracts.Infrastructure.Messaging;
using HR.Contracts.Infrastructure.Repositories;
using HR.Contracts.Infrastructure.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Integration;
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
    /// PLAN-37: the legal-hold / lifecycle gate against HR.Core (<c>GET /api/internal/employees/{id}/lifecycle-flags</c>),
    /// registered from the approved key <c>Integration:HrCoreBaseUrl</c> (the old <c>Services:HRCore:BaseUrl</c> /
    /// <c>HrCore:BaseUrl</c> still work with a start-up warning, D-INT-3). A missing URL is a start-up error outside
    /// Development. Authentication and fail-closed behaviour are PLAN-42; this plan changes only the key and the wiring.
    /// Without a configuration at all (unit tests of the infrastructure) the pass-through gate is used.
    /// </summary>
    public static IServiceCollection AddEmployeeLifecycleGate(this IServiceCollection services, IConfiguration? configuration)
    {
        if (configuration is null)
        {
            services.AddSingleton<IEmployeeLifecycleGate, PassThroughEmployeeLifecycleGate>();
            return services;
        }

        var hrCore = HrCoreEndpoint.RequireBaseUri(configuration);
        services.AddServiceEndpointWarnings();
        services.AddHttpClient(HttpEmployeeLifecycleGate.HttpClientName, client => client.BaseAddress = hrCore);
        services.AddScoped<IEmployeeLifecycleGate, HttpEmployeeLifecycleGate>();
        return services;
    }
}
