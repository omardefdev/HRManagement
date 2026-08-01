using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Ownership;

public static class OwnershipExtensions
{
    public static IServiceCollection AddOwnership(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OwnershipOptions>(configuration.GetSection(OwnershipOptions.SectionName));
        services.AddSingleton<IOwnershipProvider, OwnershipProvider>();
        services.AddSingleton<OwnershipMetrics>();
        services.AddSingleton<ISustainmentClaimGuard, SustainmentClaimGuard>();
        services.AddHostedService<OwnershipStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddOwnership(this WebApplicationBuilder builder)
    {
        builder.Services.AddOwnership(builder.Configuration);
        return builder;
    }
}
