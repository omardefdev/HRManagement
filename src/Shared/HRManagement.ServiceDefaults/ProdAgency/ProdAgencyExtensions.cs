using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ProdAgency;

public static class ProdAgencyExtensions
{
    public static IServiceCollection AddProdAgency(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProdAgencyOptions>(configuration.GetSection(ProdAgencyOptions.SectionName));
        services.AddSingleton<IProdAgencyProvider, ProdAgencyProvider>();
        services.AddSingleton<ProdAgencyMetrics>();
        services.AddSingleton<IAgencySelfAdminClaimGuard, AgencySelfAdminClaimGuard>();
        services.AddHostedService<ProdAgencyStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddProdAgency(this WebApplicationBuilder builder)
    {
        builder.Services.AddProdAgency(builder.Configuration);
        return builder;
    }
}
