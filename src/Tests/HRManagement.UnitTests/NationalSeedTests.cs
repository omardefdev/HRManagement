using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Seeding;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

public class NationalSeedTests
{
    private static HRCoreDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new HRCoreDbContext(options);
    }

    private static IServiceProvider CreateServices() =>
        new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task National_orchestrator_is_idempotent()
    {
        await using var db = CreateDb();
        var sp = CreateServices();

        await NationalSeedOrchestrator.ApplyMandatoryAsync(db, sp);
        var packCount = await db.SeedPackApplications.CountAsync();
        var wfCount = await db.WorkflowDefinitions.CountAsync();
        var mdmCount = await db.ReferenceCatalogEntries.CountAsync();

        await NationalSeedOrchestrator.ApplyMandatoryAsync(db, sp);

        Assert.Equal(packCount, await db.SeedPackApplications.CountAsync());
        Assert.Equal(wfCount, await db.WorkflowDefinitions.CountAsync());
        Assert.Equal(mdmCount, await db.ReferenceCatalogEntries.CountAsync());
        Assert.True(await db.LegislationSources.CountAsync() >= 7);
    }

    [Fact]
    public async Task Workflow_pack_seeds_all_nine_definitions()
    {
        await using var db = CreateDb();
        await WorkflowPackSeeder.SeedAsync(db);

        var codes = await db.WorkflowDefinitions.Select(d => d.Code).ToListAsync();
        foreach (var code in new[]
        {
            "ASSET-ASSIGN-APPROVAL", "ASSET-LOSS-APPROVAL", "HIRE-WIZARD", "SEPARATION-WIZARD",
            "ID-CARD-ISSUANCE", "TRANSFER-APPROVAL", "RECRUITMENT-HIRE", "DISCIPLINARY-APPROVAL", "IT-TICKET"
        })
            Assert.Contains(code, codes);
    }

    [Fact]
    public async Task Policy_workflow_bindings_reference_definitions()
    {
        await using var db = CreateDb();
        await WorkflowPackSeeder.SeedAsync(db);
        await PolicyEngineSeeder.SeedAsync(db);

        var bindings = await db.PolicyWorkflowBindings.ToListAsync();
        Assert.True(bindings.Count >= 9);

        foreach (var binding in bindings)
        {
            if (binding.WorkflowDefinitionId is null)
                continue;
            var def = await db.WorkflowDefinitions.FindAsync(binding.WorkflowDefinitionId);
            Assert.NotNull(def);
            Assert.Equal(binding.WorkflowCode, def!.Code);
        }
    }

    [Fact]
    public async Task Mdm_seeder_covers_all_catalog_kinds()
    {
        await using var db = CreateDb();
        await MdmReferenceSeeder.SeedAsync(db);

        foreach (CatalogKind kind in Enum.GetValues<CatalogKind>())
        {
            Assert.True(await db.ReferenceCatalogEntries.AnyAsync(e => e.CatalogKind == kind),
                $"Missing MDM entries for {kind}");
        }
    }

    [Fact]
    public async Task Mdm_codes_unique_per_kind_and_tenant()
    {
        await using var db = CreateDb();
        await MdmReferenceSeeder.SeedAsync(db);
        await MdmReferenceSeeder.SeedAsync(db);

        var dupes = await db.ReferenceCatalogEntries
            .GroupBy(e => new { e.TenantId, e.CatalogKind, e.Code })
            .Where(g => g.Count() > 1)
            .CountAsync();
        Assert.Equal(0, dupes);
    }

    [Fact]
    public void Seed_pack_ids_defined()
    {
        Assert.Equal("NATIONAL-MANDATORY-v1", SeedPackIds.NationalMandatory);
        Assert.Equal("NATIONAL-MDM-SY-v1", SeedPackIds.NationalMdmSyria);
    }

    [Fact]
    public void Validate_script_exists()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "seed-001-validate.ps1")));
    }
}
