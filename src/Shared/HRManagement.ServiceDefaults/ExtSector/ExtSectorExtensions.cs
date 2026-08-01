using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ExtSector;

public static class ExtSectorExtensions
{
    public static IServiceCollection AddExtSector(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExtSectorOptions>(configuration.GetSection(ExtSectorOptions.SectionName));
        services.AddSingleton<IExtSectorProvider, ExtSectorProvider>();
        services.AddSingleton<ExtSectorMetrics>();
        services.AddSingleton<ISectorCatalogClaimGuard, SectorCatalogClaimGuard>();
        services.AddHostedService<ExtSectorStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddExtSector(this WebApplicationBuilder builder)
    {
        builder.Services.AddExtSector(builder.Configuration);
        return builder;
    }
}
