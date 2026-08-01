using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.MigMatch;

public static class MigMatchExtensions
{
    public static IServiceCollection AddMigMatch(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MigMatchOptions>(configuration.GetSection(MigMatchOptions.SectionName));
        services.AddSingleton<IMigMatchProvider, MigMatchProvider>();
        services.AddSingleton<MigMatchMetrics>();
        services.AddSingleton<IIdentityMatchClaimGuard, IdentityMatchClaimGuard>();
        services.AddHostedService<MigMatchStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddMigMatch(this WebApplicationBuilder builder)
    {
        builder.Services.AddMigMatch(builder.Configuration);
        return builder;
    }
}
