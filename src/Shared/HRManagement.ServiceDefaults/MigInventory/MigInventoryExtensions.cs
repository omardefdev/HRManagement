using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.MigInventory;

public static class MigInventoryExtensions
{
    public static IServiceCollection AddMigInventory(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MigInventoryOptions>(configuration.GetSection(MigInventoryOptions.SectionName));
        services.AddSingleton<IMigInventoryProvider, MigInventoryProvider>();
        services.AddSingleton<MigInventoryMetrics>();
        services.AddSingleton<ILegacyExtractClaimGuard, LegacyExtractClaimGuard>();
        services.AddHostedService<MigInventoryStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddMigInventory(this WebApplicationBuilder builder)
    {
        builder.Services.AddMigInventory(builder.Configuration);
        return builder;
    }
}
