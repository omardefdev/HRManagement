using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Evidence;

public static class EvidenceRepositoryExtensions
{
    /// <summary>Registers EVD-003 repository against an existing DbContext that hosts EvidenceArtifacts.</summary>
    public static IServiceCollection AddEvidenceRepository<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddOptions<EvidenceRepositoryOptions>();
        services.AddScoped<IEvidenceRepositoryService>(sp =>
            new EvidenceRepositoryService(
                sp.GetRequiredService<TContext>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EvidenceRepositoryOptions>>(),
                sp.GetService<IEvidenceBlobStore>()));
        return services;
    }
}
