using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.MigOnboarding;

public static class MigOnboardingExtensions
{
    public static IServiceCollection AddMigOnboarding(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MigOnboardingOptions>(configuration.GetSection(MigOnboardingOptions.SectionName));
        services.AddSingleton<IMigOnboardingProvider, MigOnboardingProvider>();
        services.AddSingleton<MigOnboardingMetrics>();
        services.AddSingleton<IAgencyOnboardingClaimGuard, AgencyOnboardingClaimGuard>();
        services.AddHostedService<MigOnboardingStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddMigOnboarding(this WebApplicationBuilder builder)
    {
        builder.Services.AddMigOnboarding(builder.Configuration);
        return builder;
    }
}
