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

        var hrCoreBase = configuration["Integration:HrCoreBaseUrl"]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(hrCoreBase))
            services.AddServiceAuthenticatedClient(WorkflowTriggerService.HttpClientName, new Uri(hrCoreBase + "/"));
        else
            services.AddHttpClient(WorkflowTriggerService.HttpClientName);

        return services;
    }
}
