using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ProductCapability;

public static class ProductCapabilityExtensions
{
    public static IServiceCollection AddProductCapability(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CapabilityProfileOptions>(configuration.GetSection(CapabilityProfileOptions.SectionName));
        services.AddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        return services;
    }

    public static WebApplicationBuilder AddProductCapability(this WebApplicationBuilder builder)
    {
        builder.Services.AddProductCapability(builder.Configuration);
        return builder;
    }

    public static IApplicationBuilder UseCapabilityGuard(this IApplicationBuilder app) =>
        app.UseMiddleware<CapabilityGuardMiddleware>();
}
