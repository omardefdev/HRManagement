using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ProdPolicy;

public static class ProdPolicyExtensions
{
    public static IServiceCollection AddProdPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProdPolicyOptions>(configuration.GetSection(ProdPolicyOptions.SectionName));
        services.AddSingleton<IProdPolicyProvider, ProdPolicyProvider>();
        services.AddSingleton<ProdPolicyMetrics>();
        services.AddSingleton<IPolicyPlatformClaimGuard, PolicyPlatformClaimGuard>();
        services.AddHostedService<ProdPolicyStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddProdPolicy(this WebApplicationBuilder builder)
    {
        builder.Services.AddProdPolicy(builder.Configuration);
        return builder;
    }
}
