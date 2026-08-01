using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ExtApi;

public static class ExtApiExtensions
{
    public static IServiceCollection AddExtApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExtApiOptions>(configuration.GetSection(ExtApiOptions.SectionName));
        services.AddSingleton<IExtApiProvider, ExtApiProvider>();
        services.AddSingleton<ExtApiMetrics>();
        services.AddSingleton<IApiExchangeClaimGuard, ApiExchangeClaimGuard>();
        services.AddHostedService<ExtApiStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddExtApi(this WebApplicationBuilder builder)
    {
        builder.Services.AddExtApi(builder.Configuration);
        return builder;
    }
}
