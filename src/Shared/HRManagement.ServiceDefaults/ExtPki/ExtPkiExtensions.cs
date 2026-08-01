using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ExtPki;

public static class ExtPkiExtensions
{
    public static IServiceCollection AddExtPki(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExtPkiOptions>(configuration.GetSection(ExtPkiOptions.SectionName));
        services.AddSingleton<IExtPkiProvider, ExtPkiProvider>();
        services.AddSingleton<ExtPkiMetrics>();
        services.AddSingleton<IPkiSignatureClaimGuard, PkiSignatureClaimGuard>();
        services.AddHostedService<ExtPkiStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddExtPki(this WebApplicationBuilder builder)
    {
        builder.Services.AddExtPki(builder.Configuration);
        return builder;
    }
}
