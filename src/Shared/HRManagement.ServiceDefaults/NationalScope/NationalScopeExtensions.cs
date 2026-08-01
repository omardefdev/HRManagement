using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.NationalScope;

public static class NationalScopeExtensions
{
    public static IServiceCollection AddNationalScope(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NationalScopeOptions>(configuration.GetSection(NationalScopeOptions.SectionName));
        services.AddSingleton<INationalScopeProvider, NationalScopeProvider>();
        services.AddSingleton<NationalScopeMetrics>();
        services.AddSingleton<INationalScopeGuard, NationalScopeGuard>();
        services.AddHostedService<NationalScopeStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddNationalScope(this WebApplicationBuilder builder)
    {
        builder.Services.AddNationalScope(builder.Configuration);
        return builder;
    }
}
