using HR.Contracts.Application.Abstractions;
using HR.Contracts.Infrastructure.Context;
using HR.Contracts.Infrastructure.ExportJobs;
using HR.Contracts.Infrastructure.Messaging;
using HR.Contracts.Infrastructure.Repositories;
using HR.Contracts.Infrastructure.Services;
using HRManagement.ServiceDefaults;
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

        var hrCoreBase = configuration?["Services:HRCore:BaseUrl"] ?? configuration?["HrCore:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(hrCoreBase))
        {
            services.AddHttpClient("hr-core-lifecycle");
            services.AddScoped<IEmployeeLifecycleGate, HttpEmployeeLifecycleGate>();
        }
        else
        {
            services.AddSingleton<IEmployeeLifecycleGate, PassThroughEmployeeLifecycleGate>();
        }

        if (configuration is not null)
            services.AddExportJobInfrastructure(configuration);
        else
        {
            services.Configure<HRManagement.SharedKernel.Exports.ExportJobOptions>(_ => { });
            services.AddSingleton<IExportArtifactStore, LocalExportArtifactStore>();
        }

        return services;
    }
}
