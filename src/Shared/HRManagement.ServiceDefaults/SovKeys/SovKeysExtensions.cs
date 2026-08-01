using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.SovKeys;

public static class SovKeysExtensions
{
    public static IServiceCollection AddSovKeys(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SovKeysOptions>(configuration.GetSection(SovKeysOptions.SectionName));
        services.AddSingleton<ISovKeysProvider, SovKeysProvider>();
        services.AddSingleton<SovKeysMetrics>();
        services.AddSingleton<IKeyCustodyClaimGuard, KeyCustodyClaimGuard>();
        services.AddHostedService<SovKeysStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddSovKeys(this WebApplicationBuilder builder)
    {
        builder.Services.AddSovKeys(builder.Configuration);
        return builder;
    }
}
