using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ExtSandbox;

public static class ExtSandboxExtensions
{
    public static IServiceCollection AddExtSandbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExtSandboxOptions>(configuration.GetSection(ExtSandboxOptions.SectionName));
        services.AddSingleton<IExtSandboxProvider, ExtSandboxProvider>();
        services.AddSingleton<ExtSandboxMetrics>();
        services.AddSingleton<ISandboxCertClaimGuard, SandboxCertClaimGuard>();
        services.AddHostedService<ExtSandboxStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddExtSandbox(this WebApplicationBuilder builder)
    {
        builder.Services.AddExtSandbox(builder.Configuration);
        return builder;
    }
}
