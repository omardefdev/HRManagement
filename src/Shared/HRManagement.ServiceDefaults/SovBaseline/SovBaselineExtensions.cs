using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.SovBaseline;

public static class SovBaselineExtensions
{
    public static IServiceCollection AddSovBaseline(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SovBaselineOptions>(configuration.GetSection(SovBaselineOptions.SectionName));
        services.AddSingleton<ISovBaselineProvider, SovBaselineProvider>();
        services.AddSingleton<SovBaselineMetrics>();
        services.AddSingleton<IAtoClaimGuard, AtoClaimGuard>();
        services.AddHostedService<SovBaselineStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddSovBaseline(this WebApplicationBuilder builder)
    {
        builder.Services.AddSovBaseline(builder.Configuration);
        return builder;
    }
}
