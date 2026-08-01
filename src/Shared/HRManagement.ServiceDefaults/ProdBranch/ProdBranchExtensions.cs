using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ProdBranch;

public static class ProdBranchExtensions
{
    public static IServiceCollection AddProdBranch(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProdBranchOptions>(configuration.GetSection(ProdBranchOptions.SectionName));
        services.AddSingleton<IProdBranchProvider, ProdBranchProvider>();
        services.AddSingleton<ProdBranchMetrics>();
        services.AddSingleton<IRemoteBranchClaimGuard, RemoteBranchClaimGuard>();
        services.AddHostedService<ProdBranchStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddProdBranch(this WebApplicationBuilder builder)
    {
        builder.Services.AddProdBranch(builder.Configuration);
        return builder;
    }
}
