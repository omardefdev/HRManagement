using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ProdExtensibility;

public static class ProdExtensibilityExtensions
{
    public static IServiceCollection AddProdExtensibility(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProdExtensibilityOptions>(configuration.GetSection(ProdExtensibilityOptions.SectionName));
        services.AddSingleton<IProdExtensibilityProvider, ProdExtensibilityProvider>();
        services.AddSingleton<ProdExtensibilityMetrics>();
        services.AddSingleton<IExtensibilityClaimGuard, ExtensibilityClaimGuard>();
        services.AddHostedService<ProdExtensibilityStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddProdExtensibility(this WebApplicationBuilder builder)
    {
        builder.Services.AddProdExtensibility(builder.Configuration);
        return builder;
    }
}
