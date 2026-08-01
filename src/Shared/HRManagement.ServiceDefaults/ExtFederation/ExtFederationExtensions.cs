using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ExtFederation;

public static class ExtFederationExtensions
{
    public static IServiceCollection AddExtFederation(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExtFederationOptions>(configuration.GetSection(ExtFederationOptions.SectionName));
        services.AddSingleton<IExtFederationProvider, ExtFederationProvider>();
        services.AddSingleton<ExtFederationMetrics>();
        services.AddSingleton<IFederationAssuranceClaimGuard, FederationAssuranceClaimGuard>();
        services.AddHostedService<ExtFederationStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddExtFederation(this WebApplicationBuilder builder)
    {
        builder.Services.AddExtFederation(builder.Configuration);
        return builder;
    }
}
