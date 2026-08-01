using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.SovData;

public static class SovDataExtensions
{
    public static IServiceCollection AddSovData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SovDataOptions>(configuration.GetSection(SovDataOptions.SectionName));
        services.AddSingleton<ISovDataProvider, SovDataProvider>();
        services.AddSingleton<SovDataMetrics>();
        services.AddSingleton<ISovereigntyClaimGuard, SovereigntyClaimGuard>();
        services.AddHostedService<SovDataStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddSovData(this WebApplicationBuilder builder)
    {
        builder.Services.AddSovData(builder.Configuration);
        return builder;
    }
}
