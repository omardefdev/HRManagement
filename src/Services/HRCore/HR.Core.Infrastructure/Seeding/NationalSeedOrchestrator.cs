using HR.Core.Domain.Entities.Seeding;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.OrgStructure;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Seeding;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Seeding;

public static class NationalSeedOrchestrator
{
    public static async Task ApplyMandatoryAsync(
        HRCoreDbContext db,
        IServiceProvider serviceProvider,
        CancellationToken ct = default)
    {
        var tenantId = TenantDefaults.DefaultTenantId;
        var previousTenantId = TenantIdFlow.Get();
        TenantIdFlow.Set(tenantId);

        try
        {
            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("NationalSeed");
            var config = serviceProvider.GetService<IConfiguration>();

            if (await IsPackAppliedAsync(db, tenantId, SeedPackIds.NationalMandatory, SeedPackDigests.NationalMandatoryV1, ct))
            {
                logger?.LogInformation("National mandatory seed pack already applied");
                await ApplyIncrementalPatchesAsync(db, ct);
                await new OrgUnitVersionService(db, new FundedPositionVersionService(db)).BackfillMissingVersionsAsync(ct);
                await new EstablishmentAsOfService(new JobCatalogVersionService(db), new FundedPositionVersionService(db))
                    .BackfillAllMissingVersionsAsync(ct);
                return;
            }

            logger?.LogInformation("Applying SEED-001 national mandatory pack");

            await HRCoreDataSeeder.SeedAsync(db, serviceProvider, ct);
            await WorkflowPackSeeder.SeedAsync(db, ct);
            await WorkflowPlan03Seeder.EnsureMultiStepAssetAssignmentAsync(db, ct);
            await PolicyEngineSeeder.SeedAsync(db, ct);
            await CodeGenerationSeeder.SeedAsync(db, ct);
            await MdmReferenceSeeder.SeedAsync(db, ct);
            await LegislationRegisterSeeder.SeedAsync(db, ct);
            var legislationProvider = new Services.Legal.LegislationRegisterProvider(db);
            await ServiceRegimeSeeder.SeedAsync(db, legislationProvider, logger, ct);
            await BenefitPlanSeeder.SeedAsync(db, tenantId, ct);

            if (config?.GetValue("Seed:BootstrapCounters", false) == true)
            {
                var bootstrap = new CodSequenceBootstrapService(db);
                await bootstrap.BootstrapHRCoreAsync(tenantId, ct);
            }

            db.SeedPackApplications.Add(new SeedPackApplication
            {
                TenantId = tenantId,
                PackId = SeedPackIds.NationalMandatory,
                ReleaseDigest = SeedPackDigests.NationalMandatoryV1,
                AppliedAt = DateTime.UtcNow,
                RollbackRef = "seed-rollback-national-v1"
            });
            await db.SaveChangesAsync(ct);

            await new OrgUnitVersionService(db, new FundedPositionVersionService(db)).BackfillMissingVersionsAsync(ct);
            await new EstablishmentAsOfService(new JobCatalogVersionService(db), new FundedPositionVersionService(db))
                .BackfillAllMissingVersionsAsync(ct);

            logger?.LogInformation("National mandatory seed pack applied");
        }
        finally
        {
            TenantIdFlow.Set(previousTenantId);
        }
    }

    private static async Task ApplyIncrementalPatchesAsync(HRCoreDbContext db, CancellationToken ct)
    {
        await WorkflowPackSeeder.SeedAsync(db, ct);
        await WorkflowPlan03Seeder.EnsureMultiStepAssetAssignmentAsync(db, ct);
        await PolicyEngineSeeder.SeedAsync(db, ct);
        await CodeGenerationSeeder.SeedAsync(db, ct);
        await MdmReferenceSeeder.SeedAsync(db, ct);
        await LegislationRegisterSeeder.SeedAsync(db, ct);
        var legislationProvider = new Services.Legal.LegislationRegisterProvider(db);
        await ServiceRegimeSeeder.SeedAsync(db, legislationProvider, ct: ct);
        await BenefitPlanSeeder.SeedDefaultTenantAsync(db, ct);
        await ServiceRegimeBackfillService.SeedAsync(db, ct);
    }

    private static Task<bool> IsPackAppliedAsync(
        HRCoreDbContext db,
        Guid tenantId,
        string packId,
        string digest,
        CancellationToken ct) =>
        db.SeedPackApplications
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .AnyAsync(
            p => p.TenantId == tenantId && p.PackId == packId && p.ReleaseDigest == digest, ct);
}
