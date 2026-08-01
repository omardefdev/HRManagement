using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ModuleCatalog;

public static class ModuleCatalogExtensions
{
    public static IServiceCollection AddModuleCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ModuleCatalogOptions>(configuration.GetSection(ModuleCatalogOptions.SectionName));
        services.AddSingleton<IModuleCatalogProvider, ModuleCatalogProvider>();
        services.AddSingleton<ModuleCatalogMetrics>();
        services.AddSingleton<IModuleEntitlementGuard, ModuleEntitlementGuard>();
        services.AddHostedService<ModuleCatalogStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddModuleCatalog(this WebApplicationBuilder builder)
    {
        builder.Services.AddModuleCatalog(builder.Configuration);
        return builder;
    }
}
