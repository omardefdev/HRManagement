using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.OrgStructure;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRManagement.UnitTests;

/// <summary>PLAN-04 W4.2: OrgUnitVersionService temporal behavior.</summary>
public class OrgUnitVersionServiceTests
{
    [Fact]
    public async Task Rename_closes_prior_version_and_opens_new()
    {
        await using var db = CreateDb();
        var svc = CreateService(db);
        var unit = await SeedUnitAsync(db, "الفرع الأول");

        await svc.AppendVersionAsync(unit, OrgOperationType.Create, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        unit.NameAr = "الفرع الأول — محدّث";
        await svc.AppendVersionAsync(unit, OrgOperationType.Rename, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var versions = await db.OrgUnitVersions.Where(v => v.OrgUnitId == unit.Id).OrderBy(v => v.ValidFrom).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), versions[0].ValidTo);
        Assert.Null(versions[1].ValidTo);
        Assert.Equal("الفرع الأول — محدّث", versions[1].Name);
    }

    [Fact]
    public async Task AsOf_before_rename_returns_old_name_and_head()
    {
        await using var db = CreateDb();
        var svc = CreateService(db);
        var headId = Guid.NewGuid();
        var unit = await SeedUnitAsync(db, "الفرع الأول", headId);

        await svc.AppendVersionAsync(unit, OrgOperationType.Create, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        unit.NameAr = "اسم جديد";
        unit.HeadEmployeeId = null;
        await svc.AppendVersionAsync(unit, OrgOperationType.Rename, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var before = await svc.ResolveAsOfAsync(unit.Id, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var after = await svc.ResolveAsOfAsync(unit.Id, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(before);
        Assert.Equal("الفرع الأول", before!.OrgUnit.NameAr);
        Assert.Equal(headId, before.OrgUnit.HeadEmployeeId);
        Assert.NotNull(after);
        Assert.Equal("اسم جديد", after!.OrgUnit.NameAr);
        Assert.Null(after.OrgUnit.HeadEmployeeId);
    }

    [Fact]
    public async Task Backfill_creates_create_version_for_existing_units()
    {
        await using var db = CreateDb();
        var unit = await SeedUnitAsync(db, "وحدة بلا نسخة");
        var svc = CreateService(db);

        var count = await svc.BackfillMissingVersionsAsync();
        Assert.Equal(1, count);

        var version = await db.OrgUnitVersions.SingleAsync(v => v.OrgUnitId == unit.Id);
        Assert.Equal(OrgOperationType.Create, version.OperationType);
        Assert.Equal(unit.NameAr, version.Name);
    }

    private static OrgUnitVersionService CreateService(HRCoreDbContext db) =>
        new(db, new FundedPositionVersionService(db));

    private static async Task<OrgUnit> SeedUnitAsync(HRCoreDbContext db, string nameAr, Guid? headId = null)
    {
        var tenantId = AcceptanceSeedIds.AgencyAId;
        var unit = new OrgUnit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "TST",
            NameAr = nameAr,
            NameEn = "TST",
            HeadEmployeeId = headId,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.OrgUnits.Add(unit);
        await db.SaveChangesAsync();
        return unit;
    }

    private static HRCoreDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new HRCoreDbContext(options, DesignTimeTenantContext.Instance);
        db.Database.EnsureCreated();
        return db;
    }
}
