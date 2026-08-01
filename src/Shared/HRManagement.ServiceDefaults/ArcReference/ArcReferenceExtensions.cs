using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ArcReference;

public static class ArcReferenceExtensions
{
    public static IServiceCollection AddArcReference(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ArcReferenceOptions>(configuration.GetSection(ArcReferenceOptions.SectionName));
        services.AddSingleton<IArcReferenceProvider, ArcReferenceProvider>();
        services.AddSingleton<ArcReferenceMetrics>();
        services.AddSingleton<IReferenceClaimGuard, ReferenceClaimGuard>();
        services.AddHostedService<ArcReferenceStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddArcReference(this WebApplicationBuilder builder)
    {
        builder.Services.AddArcReference(builder.Configuration);
        return builder;
    }
}
