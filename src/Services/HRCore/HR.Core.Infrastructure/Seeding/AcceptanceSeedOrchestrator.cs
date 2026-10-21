using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.OrgStructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Seeding;

public static class AcceptanceSeedOrchestrator
{
    public static async Task ApplyAsync(HRCoreDbContext db, IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("AcceptanceSeed");
        logger?.LogInformation("Applying acceptance seed pack (PLAN-00)");
        await AcceptanceDataSeeder.SeedAsync(db, logger, ct);
        await AcceptanceMdmReferencePatch.ApplyAsync(db, logger, ct);
        await AcceptanceTemporalPatch.ApplyAsync(db, logger, ct);
        await AcceptancePlanningPatch.ApplyAsync(db, logger, ct);
        await new OrgUnitVersionService(db, serviceProvider.GetRequiredService<IFundedPositionVersionService>())
            .BackfillMissingVersionsAsync(ct);
        var asOf = serviceProvider.GetRequiredService<EstablishmentAsOfService>();
        await asOf.BackfillAllMissingVersionsAsync(ct);
    }
}