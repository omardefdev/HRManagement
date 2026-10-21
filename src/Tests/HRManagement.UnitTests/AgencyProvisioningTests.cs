using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Onboarding;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.TestSupport;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

public class AgencyProvisioningTests
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

    private static AgencyProvisioningService CreateSvc(HRCoreDbContext db, Mock<IPublishEndpoint>? publishMock = null)
    {
        var publish = publishMock?.Object ?? Mock.Of<IPublishEndpoint>();
        var path = new HR.Core.Infrastructure.OrgUnitPathService(db);
        var integration = new OrgUnitIntegrationService(db, publish);
        var orgProv = new AgencyOrgStructureProvisioner(db, path, integration);
        return new AgencyProvisioningService(db, CreateServices(), orgProv, publish, NullLogger<AgencyProvisioningService>.Instance);
    }

    [Fact]
    public void Example_manifest_loads_and_validates()
    {
        var path = RepoPaths.Resolve("seed-packs", "examples", "agency-ministry-finance.json");
        var manifest = AgencyManifestLoader.LoadFromFile(path);
        Assert.Equal("agency-ministry-finance", manifest.AgencyId);
        Assert.True(manifest.Policies.PreserveNationalMandatory);
        Assert.True(manifest.ReviewerRoles.Count >= 2);
        Assert.Equal(2, manifest.OrgStructure.Units.Count);
    }

    [Fact]
    public void Acceptance_manifest_loads_org_tree()
    {
        var path = RepoPaths.Resolve("seed-packs", "examples", "agency-acceptance-test.json");
        var manifest = AgencyManifestLoader.LoadFromFile(path);
        Assert.Equal("agency-acceptance-test", manifest.AgencyId);
        Assert.Equal(2, manifest.OrgStructure.Units.Count);
    }

    [Fact]
    public void Manifest_rejects_org_cycle()
    {
        var manifest = ValidManifest();
        manifest.OrgStructure.Units =
        [
            new AgencyOrgUnitDef { Code = "A", ParentCode = "B", NameAr = "أ" },
            new AgencyOrgUnitDef { Code = "B", ParentCode = "A", NameAr = "ب" }
        ];

        Assert.Throws<InvalidOperationException>(() => AgencyManifestLoader.Validate(manifest));
    }

    [Fact]
    public void Manifest_rejects_missing_four_eyes()
    {
        var manifest = new AgencyManifest
        {
            PackId = "test",
            AgencyId = "test-agency",
            ReleaseDigest = "sha256:test",
            ReviewerRoles = ["only-one"],
            Policies = new AgencyPoliciesSection { PolicyPackIds = ["p1"], PreserveNationalMandatory = true },
            ReferenceSubset = new AgencyReferenceSubsetSection { CatalogKinds = ["Grade"] },
            Tenant = new AgencyTenantSection { DisplayName = "Test" }
        };

        Assert.Throws<InvalidOperationException>(() => AgencyManifestLoader.Validate(manifest));
    }

    [Fact]
    public void Manifest_rejects_preserveNationalMandatory_false()
    {
        var manifest = new AgencyManifest
        {
            PackId = "test",
            AgencyId = "test-agency",
            ReleaseDigest = "sha256:test",
            ReviewerRoles = ["a", "b"],
            Policies = new AgencyPoliciesSection { PolicyPackIds = ["p1"], PreserveNationalMandatory = false },
            ReferenceSubset = new AgencyReferenceSubsetSection { CatalogKinds = ["Grade"] },
            Tenant = new AgencyTenantSection { DisplayName = "Test" }
        };

        Assert.Throws<InvalidOperationException>(() => AgencyManifestLoader.Validate(manifest));
    }

    [Fact]
    public async Task Dry_run_does_not_persist_tenant()
    {
        await using var db = CreateDb();
        await MdmReferenceSeeder.SeedAsync(db, TenantDefaults.DefaultTenantId);

        var path = RepoPaths.Resolve("seed-packs", "examples", "agency-ministry-finance.json");
        var manifest = AgencyManifestLoader.LoadFromFile(path);

        var svc = CreateSvc(db);
        var result = await svc.ApplyManifestAsync(manifest, dryRun: true);

        Assert.True(result.DryRun);
        Assert.Null(result.TenantId);
        Assert.Equal(0, await db.Tenants.CountAsync(t => t.Code == "AGENCY-MINISTRY-FINANCE"));
    }

    [Fact]
    public async Task Apply_copies_mdm_subset_to_new_tenant()
    {
        await using var db = CreateDb();
        await MdmReferenceSeeder.SeedAsync(db, TenantDefaults.DefaultTenantId);

        var path = RepoPaths.Resolve("seed-packs", "examples", "agency-ministry-finance.json");
        var manifest = AgencyManifestLoader.LoadFromFile(path);

        var svc = CreateSvc(db);
        var result = await svc.ApplyManifestAsync(manifest, dryRun: false);

        Assert.NotNull(result.TenantId);
        var tenantId = result.TenantId!.Value;

        Assert.True(await db.ReferenceCatalogEntries.AnyAsync(
            e => e.TenantId == tenantId && e.CatalogKind == CatalogKind.Grade));
        Assert.True(await db.ReferenceCatalogEntries.AnyAsync(
            e => e.TenantId == tenantId && e.CatalogKind == CatalogKind.Bank));
    }

    [Fact]
    public async Task Apply_creates_org_tree()
    {
        await using var db = CreateDb();
        await MdmReferenceSeeder.SeedAsync(db, TenantDefaults.DefaultTenantId);

        var manifest = ValidManifest();
        manifest.OrgStructure.Units =
        [
            new AgencyOrgUnitDef { Code = "HQ", NameAr = "المركز" },
            new AgencyOrgUnitDef { Code = "BR1", ParentCode = "HQ", NameAr = "فرع 1" }
        ];

        var svc = CreateSvc(db);
        var result = await svc.ApplyManifestAsync(manifest, dryRun: false);

        Assert.NotNull(result.TenantId);
        var tenantId = result.TenantId!.Value;
        Assert.Equal(2, await db.OrgUnits.CountAsync(o => o.TenantId == tenantId));
        Assert.True(await db.OrgUnits.AnyAsync(o => o.TenantId == tenantId && o.Code == "HQ" && o.ParentId == null));
        Assert.True(await db.OrgUnits.AnyAsync(o => o.TenantId == tenantId && o.Code == "BR1" && o.ParentId != null));
    }

    [Fact]
    public async Task Apply_records_seed_pack_application()
    {
        await using var db = CreateDb();
        await MdmReferenceSeeder.SeedAsync(db, TenantDefaults.DefaultTenantId);

        var path = RepoPaths.Resolve("seed-packs", "examples", "agency-ministry-finance.json");
        var manifest = AgencyManifestLoader.LoadFromFile(path);

        var svc = CreateSvc(db);
        await svc.ApplyManifestAsync(manifest, dryRun: false);

        Assert.True(await db.SeedPackApplications.AnyAsync(
            p => p.PackId == manifest.PackId && p.ReleaseDigest == manifest.ReleaseDigest));
    }

    [Fact]
    public async Task Apply_twice_does_not_duplicate_org_units_or_seed_pack()
    {
        await using var db = CreateDb();
        await MdmReferenceSeeder.SeedAsync(db, TenantDefaults.DefaultTenantId);

        var manifest = ValidManifest();
        manifest.OrgStructure.Units =
        [
            new AgencyOrgUnitDef { Code = "HQ", NameAr = "المركز" },
            new AgencyOrgUnitDef { Code = "BR1", ParentCode = "HQ", NameAr = "فرع 1" }
        ];

        var publish = new Mock<IPublishEndpoint>();
        var svc = CreateSvc(db, publish);

        await svc.ApplyManifestAsync(manifest, dryRun: false);
        var orgAfterFirst = await db.OrgUnits.CountAsync();
        var packsAfterFirst = await db.SeedPackApplications.CountAsync();
        var mdmAfterFirst = await db.ReferenceCatalogEntries.CountAsync();

        var second = await svc.ApplyManifestAsync(manifest, dryRun: false);

        Assert.True(second.AlreadyApplied);
        Assert.Equal(orgAfterFirst, await db.OrgUnits.CountAsync());
        Assert.Equal(packsAfterFirst, await db.SeedPackApplications.CountAsync());
        Assert.Equal(mdmAfterFirst, await db.ReferenceCatalogEntries.CountAsync());
        Assert.Equal(1, CountAgencyOnboardedPublishes(publish));
    }

    [Fact]
    public async Task AlreadyApplied_returns_without_side_effects()
    {
        await using var db = CreateDb();
        await MdmReferenceSeeder.SeedAsync(db, TenantDefaults.DefaultTenantId);

        var manifest = ValidManifest();
        var svc = CreateSvc(db);
        var first = await svc.ApplyManifestAsync(manifest, dryRun: false);
        Assert.NotNull(first.TenantId);

        var tenantsBefore = await db.Tenants.CountAsync();
        var second = await svc.ApplyManifestAsync(manifest, dryRun: false);

        Assert.True(second.AlreadyApplied);
        Assert.Equal(tenantsBefore, await db.Tenants.CountAsync());
        Assert.Contains("AlreadyApplied", second.Warnings);
    }

    [Fact]
    public async Task Apply_publishes_AgencyOnboarded()
    {
        await using var db = CreateDb();
        await MdmReferenceSeeder.SeedAsync(db, TenantDefaults.DefaultTenantId);

        var manifest = ValidManifest();
        manifest.OrgStructure.Units = [new AgencyOrgUnitDef { Code = "HQ", NameAr = "المركز" }];

        var publish = new Mock<IPublishEndpoint>();
        var svc = CreateSvc(db, publish);
        await svc.ApplyManifestAsync(manifest, dryRun: false);

        Assert.Equal(1, CountAgencyOnboardedPublishes(publish));
    }

    private static int CountAgencyOnboardedPublishes(Mock<IPublishEndpoint> publish) =>
        publish.Invocations.Count(i =>
            i.Method.Name == "Publish"
            && i.Method.IsGenericMethod
            && i.Method.GetGenericArguments()[0] == typeof(IAgencyOnboarded));

    private static AgencyManifest ValidManifest() => new()
    {
        PackId = "test-pack",
        AgencyId = "test-agency-onb",
        ReleaseDigest = "sha256:test-onb",
        ReviewerRoles = ["a", "b"],
        RollbackRef = "rollback/test",
        Policies = new AgencyPoliciesSection { PolicyPackIds = ["policy-test"], CodePackIds = [], PreserveNationalMandatory = true },
        ReferenceSubset = new AgencyReferenceSubsetSection { CatalogKinds = ["Grade"] },
        Tenant = new AgencyTenantSection { DisplayName = "Test Agency" }
    };
}
