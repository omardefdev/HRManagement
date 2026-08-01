using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.SovZt;

public static class SovZtExtensions
{
    public static IServiceCollection AddSovZt(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SovZtOptions>(configuration.GetSection(SovZtOptions.SectionName));
        services.AddSingleton<ISovZtProvider, SovZtProvider>();
        services.AddSingleton<SovZtMetrics>();
        services.AddSingleton<IPamClaimGuard, PamClaimGuard>();
        services.AddHostedService<SovZtStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddSovZt(this WebApplicationBuilder builder)
    {
        builder.Services.AddSovZt(builder.Configuration);
        return builder;
    }
}
