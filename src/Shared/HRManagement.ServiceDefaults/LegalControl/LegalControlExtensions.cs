using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.LegalControl;

public static class LegalControlExtensions
{
    public static IServiceCollection AddLegalControl(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LegalControlOptions>(configuration.GetSection(LegalControlOptions.SectionName));
        services.AddSingleton<ILegalControlCatalogProvider, LegalControlCatalogProvider>();
        services.AddSingleton<LegalControlMetrics>();
        services.AddSingleton<ILegalControlGuard, LegalControlGuard>();
        services.AddHostedService<LegalControlStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddLegalControl(this WebApplicationBuilder builder)
    {
        builder.Services.AddLegalControl(builder.Configuration);
        return builder;
    }
}
