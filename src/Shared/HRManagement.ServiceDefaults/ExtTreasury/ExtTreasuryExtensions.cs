using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ExtTreasury;

public static class ExtTreasuryExtensions
{
    public static IServiceCollection AddExtTreasury(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExtTreasuryOptions>(configuration.GetSection(ExtTreasuryOptions.SectionName));
        services.AddSingleton<IExtTreasuryProvider, ExtTreasuryProvider>();
        services.AddSingleton<ExtTreasuryMetrics>();
        services.AddSingleton<ITreasuryPaymentClaimGuard, TreasuryPaymentClaimGuard>();
        services.AddHostedService<ExtTreasuryStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddExtTreasury(this WebApplicationBuilder builder)
    {
        builder.Services.AddExtTreasury(builder.Configuration);
        return builder;
    }
}
