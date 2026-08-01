using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.NoFork;

public static class NoForkExtensions
{
    public static IServiceCollection AddNoForkPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NoForkOptions>(configuration.GetSection(NoForkOptions.SectionName));
        services.AddSingleton<INoForkPolicyProvider, NoForkPolicyProvider>();
        services.AddSingleton<IAgencyConfigResolver, AgencyConfigResolver>();
        services.AddSingleton<NoForkMetrics>();
        services.AddSingleton<IAgencyBrandingAccessor, AgencyBrandingAccessor>();
        services.AddHostedService<NoForkStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddNoForkPolicy(this WebApplicationBuilder builder)
    {
        builder.Services.AddNoForkPolicy(builder.Configuration);
        return builder;
    }
}
