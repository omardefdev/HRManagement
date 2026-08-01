using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.MigCutover;

public static class MigCutoverExtensions
{
    public static IServiceCollection AddMigCutover(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MigCutoverOptions>(configuration.GetSection(MigCutoverOptions.SectionName));
        services.AddSingleton<IMigCutoverProvider, MigCutoverProvider>();
        services.AddSingleton<MigCutoverMetrics>();
        services.AddSingleton<IAgencyCutoverClaimGuard, AgencyCutoverClaimGuard>();
        services.AddHostedService<MigCutoverStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddMigCutover(this WebApplicationBuilder builder)
    {
        builder.Services.AddMigCutover(builder.Configuration);
        return builder;
    }
}
