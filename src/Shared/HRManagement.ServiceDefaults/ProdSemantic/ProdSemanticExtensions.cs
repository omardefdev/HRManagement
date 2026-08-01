using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ProdSemantic;

public static class ProdSemanticExtensions
{
    public static IServiceCollection AddProdSemantic(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProdSemanticOptions>(configuration.GetSection(ProdSemanticOptions.SectionName));
        services.AddSingleton<IProdSemanticProvider, ProdSemanticProvider>();
        services.AddSingleton<ProdSemanticMetrics>();
        services.AddSingleton<ISemanticLayerClaimGuard, SemanticLayerClaimGuard>();
        services.AddHostedService<ProdSemanticStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddProdSemantic(this WebApplicationBuilder builder)
    {
        builder.Services.AddProdSemantic(builder.Configuration);
        return builder;
    }
}
