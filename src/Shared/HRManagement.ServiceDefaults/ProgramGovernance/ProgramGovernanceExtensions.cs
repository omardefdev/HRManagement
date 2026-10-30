using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HRManagement.ServiceDefaults.ProgramGovernance;

public static class ProgramGovernanceExtensions
{
    public static IServiceCollection AddProgramGovernance(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProgramGovernanceOptions>(configuration.GetSection(ProgramGovernanceOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IProgramGovernanceProvider, ProgramGovernanceProvider>();
        services.AddSingleton<ProgramGovernanceMetrics>();
        services.AddSingleton<IGoLiveSeatGuard, GoLiveSeatGuard>();
        services.AddHostedService<ProgramGovernanceStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddProgramGovernance(this WebApplicationBuilder builder)
    {
        builder.Services.AddProgramGovernance(builder.Configuration);
        return builder;
    }
}
