using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.MigFactory;

public static class MigFactoryExtensions
{
    public static IServiceCollection AddMigFactory(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MigFactoryOptions>(configuration.GetSection(MigFactoryOptions.SectionName));
        services.AddSingleton<IMigFactoryProvider, MigFactoryProvider>();
        services.AddSingleton<MigFactoryMetrics>();
        services.AddSingleton<IEtlWorkersClaimGuard, EtlWorkersClaimGuard>();
        services.AddHostedService<MigFactoryStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddMigFactory(this WebApplicationBuilder builder)
    {
        builder.Services.AddMigFactory(builder.Configuration);
        return builder;
    }
}
