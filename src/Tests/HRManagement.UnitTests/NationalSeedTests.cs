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
    private static HRCoreDbContext CreateDb(string? name = null)
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;
        return new HRCoreDbContext(options);
    }

    /// <summary>
    /// PLAN-48: the orchestrator's code policy upgrade reseeds each tenant in a scope of its own, so the provider registers
    /// HRCoreDbContext (on the same database), as the application's does.
    /// </summary>
    private static IServiceProvider CreateServices(string name) =>
        new ServiceCollection()
            .AddDbContext<HRCoreDbContext>(o => o.UseInMemoryDatabase(name))
            .BuildServiceProvider();

    [Fact]
    public async Task National_orchestrator_is_idempotent()
    {
        var name = Guid.NewGuid().ToString();
        await using var db = CreateDb(name);
        var sp = CreateServices(name);

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

        // PLAN-49 (plan section 3): a new database binds the codes a rule of a governed domain may name, and no other (it bound
        // thirteen before, seven of them for domains with no call site or for retired codes). Pol049FreshSeedTests compares the
        // seven with the catalog.
        var bindings = await db.PolicyWorkflowBindings.ToListAsync();
        Assert.Equal(7, bindings.Count);
        Assert.All(bindings, b => Assert.True(HRManagement.SharedKernel.Policy.PolicyDomainCatalog.IsGoverned(b.Domain), b.Domain));

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
