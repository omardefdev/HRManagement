using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ProdEss;

public static class ProdEssExtensions
{
    public static IServiceCollection AddProdEss(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProdEssOptions>(configuration.GetSection(ProdEssOptions.SectionName));
        services.AddSingleton<IProdEssProvider, ProdEssProvider>();
        services.AddSingleton<ProdEssMetrics>();
        services.AddSingleton<IEmployeeSelfServiceClaimGuard, EmployeeSelfServiceClaimGuard>();
        services.AddHostedService<ProdEssStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddProdEss(this WebApplicationBuilder builder)
    {
        builder.Services.AddProdEss(builder.Configuration);
        return builder;
    }
}
