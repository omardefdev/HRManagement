using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.SovThreat;

public static class SovThreatExtensions
{
    public static IServiceCollection AddSovThreat(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SovThreatOptions>(configuration.GetSection(SovThreatOptions.SectionName));
        services.AddSingleton<ISovThreatProvider, SovThreatProvider>();
        services.AddSingleton<SovThreatMetrics>();
        services.AddSingleton<IThreatModelClaimGuard, ThreatModelClaimGuard>();
        services.AddHostedService<SovThreatStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddSovThreat(this WebApplicationBuilder builder)
    {
        builder.Services.AddSovThreat(builder.Configuration);
        return builder;
    }
}
