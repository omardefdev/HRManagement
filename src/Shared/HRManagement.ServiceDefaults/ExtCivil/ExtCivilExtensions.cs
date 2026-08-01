using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ExtCivil;

public static class ExtCivilExtensions
{
    public static IServiceCollection AddExtCivil(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExtCivilOptions>(configuration.GetSection(ExtCivilOptions.SectionName));
        services.AddSingleton<IExtCivilProvider, ExtCivilProvider>();
        services.AddSingleton<ExtCivilMetrics>();
        services.AddSingleton<ICivilRegistryClaimGuard, CivilRegistryClaimGuard>();
        services.AddHostedService<ExtCivilStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddExtCivil(this WebApplicationBuilder builder)
    {
        builder.Services.AddExtCivil(builder.Configuration);
        return builder;
    }
}
