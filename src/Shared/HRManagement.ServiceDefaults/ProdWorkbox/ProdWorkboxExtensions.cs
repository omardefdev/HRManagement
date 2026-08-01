using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ProdWorkbox;

public static class ProdWorkboxExtensions
{
    public static IServiceCollection AddProdWorkbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProdWorkboxOptions>(configuration.GetSection(ProdWorkboxOptions.SectionName));
        services.AddSingleton<IProdWorkboxProvider, ProdWorkboxProvider>();
        services.AddSingleton<ProdWorkboxMetrics>();
        services.AddSingleton<IWorkboxClaimGuard, WorkboxClaimGuard>();
        services.AddHostedService<ProdWorkboxStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddProdWorkbox(this WebApplicationBuilder builder)
    {
        builder.Services.AddProdWorkbox(builder.Configuration);
        return builder;
    }
}
