using Admin.Logistics.Infrastructure.Abstractions;
using Admin.Logistics.Infrastructure.Integration;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Admin.Logistics.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAdminLogisticsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IEmployeeLookup, EmployeeLookupService>();
        services.AddScoped<IAssetClassifier, AssetClassifier>();
        services.AddScoped<WorkflowTriggerService>();
        services.AddHrPolicyEnforceClient(configuration);
        services.AddHrCodeGeneration(configuration);
        services.AddHrCoreLookupClient(configuration);
        services.AddHrCoreWorkflowClient(configuration);
        return services;
    }

    /// <summary>PLAN-37: the HR.Core workflow-start client (<c>POST api/workflow-instances</c>, scope hr-core.workflow).</summary>
    public static IServiceCollection AddHrCoreWorkflowClient(this IServiceCollection services, IConfiguration configuration)
    {
        // PLAN-37: required URL (start-up error naming Integration:HrCoreBaseUrl outside Development).
        services.AddServiceEndpointWarnings();
        services.AddServiceAuthenticatedClient(WorkflowTriggerService.HttpClientName, HrCoreEndpoint.RequireBaseUri(configuration));
        return services;
    }
}
