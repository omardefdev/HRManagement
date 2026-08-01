using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.MigEvidence;

public static class MigEvidenceExtensions
{
    public static IServiceCollection AddMigEvidence(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MigEvidenceOptions>(configuration.GetSection(MigEvidenceOptions.SectionName));
        services.AddSingleton<IMigEvidenceProvider, MigEvidenceProvider>();
        services.AddSingleton<MigEvidenceMetrics>();
        services.AddSingleton<IAgencyReconClaimGuard, AgencyReconClaimGuard>();
        services.AddHostedService<MigEvidenceStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddMigEvidence(this WebApplicationBuilder builder)
    {
        builder.Services.AddMigEvidence(builder.Configuration);
        return builder;
    }
}
