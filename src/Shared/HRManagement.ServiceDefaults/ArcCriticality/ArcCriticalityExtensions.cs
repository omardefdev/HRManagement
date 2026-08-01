using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ArcCriticality;

public static class ArcCriticalityExtensions
{
    public static IServiceCollection AddArcCriticality(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ArcCriticalityOptions>(configuration.GetSection(ArcCriticalityOptions.SectionName));
        services.AddSingleton<IArcCriticalityProvider, ArcCriticalityProvider>();
        services.AddSingleton<ArcCriticalityMetrics>();
        services.AddSingleton<ICriticalityClaimGuard, CriticalityClaimGuard>();
        services.AddHostedService<ArcCriticalityStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddArcCriticality(this WebApplicationBuilder builder)
    {
        builder.Services.AddArcCriticality(builder.Configuration);
        return builder;
    }
}
