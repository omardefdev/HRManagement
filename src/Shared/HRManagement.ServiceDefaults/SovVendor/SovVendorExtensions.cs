using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.SovVendor;

public static class SovVendorExtensions
{
    public static IServiceCollection AddSovVendor(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SovVendorOptions>(configuration.GetSection(SovVendorOptions.SectionName));
        services.AddSingleton<ISovVendorProvider, SovVendorProvider>();
        services.AddSingleton<SovVendorMetrics>();
        services.AddSingleton<IVendorClaimGuard, VendorClaimGuard>();
        services.AddHostedService<SovVendorStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddSovVendor(this WebApplicationBuilder builder)
    {
        builder.Services.AddSovVendor(builder.Configuration);
        return builder;
    }
}
