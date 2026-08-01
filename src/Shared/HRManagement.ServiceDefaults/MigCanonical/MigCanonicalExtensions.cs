using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.MigCanonical;

public static class MigCanonicalExtensions
{
    public static IServiceCollection AddMigCanonical(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MigCanonicalOptions>(configuration.GetSection(MigCanonicalOptions.SectionName));
        services.AddSingleton<IMigCanonicalProvider, MigCanonicalProvider>();
        services.AddSingleton<MigCanonicalMetrics>();
        services.AddSingleton<ICodeMapClaimGuard, CodeMapClaimGuard>();
        services.AddHostedService<MigCanonicalStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddMigCanonical(this WebApplicationBuilder builder)
    {
        builder.Services.AddMigCanonical(builder.Configuration);
        return builder;
    }
}
