using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.MasterData;
using HRManagement.ServiceDefaults.Tenancy;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class Est007ReferenceCatalogAdminTests
{
    private static readonly Guid AgencyTenantId = Guid.Parse("a0000001-0000-4000-8000-000000000001");

    [Fact]
    public async Task Create_adds_grade_on_agency_tenant()
    {
        await using var db = CreateContext(AgencyTenantId);
        var service = new ReferenceCatalogAdminService(db);

        var (entry, error) = await service.CreateAsync(
            CatalogKind.Grade,
            "GR-16",
            "الدرجة السادسة عشر",
            "Grade 16",
            null,
            null);

        Assert.Null(error);
        Assert.NotNull(entry);
        Assert.Equal("GR-16", entry!.Code);
        Assert.Equal(AgencyTenantId, entry.TenantId);
    }

    [Fact]
    public async Task Create_rejects_duplicate_code_per_tenant()
    {
        await using var db = CreateContext(AgencyTenantId);
        db.ReferenceCatalogEntries.Add(new ReferenceCatalogEntry
        {
            TenantId = AgencyTenantId,
            CatalogKind = CatalogKind.Grade,
            Code = "GR-05",
            NameAr = "موجود",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new ReferenceCatalogAdminService(db);
        var (_, error) = await service.CreateAsync(
            CatalogKind.Grade,
            "GR-05",
            "مكرر",
            null,
            null,
            null);

        Assert.NotNull(error);
        Assert.Contains("GR-05", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanMutate_blocks_platform_entries()
    {
        await using var db = CreateContext(AgencyTenantId);
        var platformEntry = new ReferenceCatalogEntry
        {
            TenantId = TenantDefaults.DefaultTenantId,
            CatalogKind = CatalogKind.Grade,
            Code = "GR-PLAT",
            NameAr = "منصة",
            IsActive = true
        };
        db.ReferenceCatalogEntries.Add(platformEntry);
        await db.SaveChangesAsync();

        var service = new ReferenceCatalogAdminService(db);
        Assert.False(service.CanMutate(platformEntry));
    }

    [Fact]
    public async Task Deactivate_rejects_grade_used_in_job_catalog()
    {
        await using var db = CreateContext(AgencyTenantId);
        var grade = new ReferenceCatalogEntry
        {
            TenantId = AgencyTenantId,
            CatalogKind = CatalogKind.Grade,
            Code = "GR-05",
            NameAr = "الدرجة الخامسة",
            IsActive = true
        };
        db.ReferenceCatalogEntries.Add(grade);
        db.JobCatalogEntries.Add(new JobCatalogEntry
        {
            TenantId = AgencyTenantId,
            JobCode = "TEST-001",
            TitleAr = "اختبار",
            GradeCode = "GR-05",
            OccupationalGroup = "JC-ADM",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new ReferenceCatalogAdminService(db);
        var (success, error) = await service.DeactivateAsync(grade.Id, null);

        Assert.False(success);
        Assert.Contains("JobCatalogEntry", error, StringComparison.Ordinal);
        Assert.True(await db.ReferenceCatalogEntries.Where(e => e.Id == grade.Id).Select(e => e.IsActive).FirstAsync());
    }

    [Fact]
    public async Task AnalyzeImpact_counts_job_catalog_grade_references()
    {
        await using var db = CreateContext(AgencyTenantId);
        var grade = new ReferenceCatalogEntry
        {
            TenantId = AgencyTenantId,
            CatalogKind = CatalogKind.Grade,
            Code = "GR-05",
            NameAr = "الدرجة الخامسة",
            IsActive = true
        };
        db.ReferenceCatalogEntries.Add(grade);
        db.JobCatalogEntries.Add(new JobCatalogEntry
        {
            TenantId = AgencyTenantId,
            JobCode = "TEST-002",
            TitleAr = "اختبار 2",
            GradeCode = "GR-05",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new ReferenceCatalogAdminService(db);
        var impact = await service.AnalyzeImpactAsync(grade.Id);

        Assert.Contains(impact, i => i.Entity == "JobCatalogEntry" && i.AffectedCount == 1);
    }

    [Fact]
    public async Task Deactivate_rejects_invalid_replacement_code()
    {
        await using var db = CreateContext(AgencyTenantId);
        var grade = new ReferenceCatalogEntry
        {
            TenantId = AgencyTenantId,
            CatalogKind = CatalogKind.Grade,
            Code = "GR-99",
            NameAr = "درجة للتعطيل",
            IsActive = true
        };
        db.ReferenceCatalogEntries.Add(grade);
        await db.SaveChangesAsync();

        var service = new ReferenceCatalogAdminService(db);
        var (success, error) = await service.DeactivateAsync(grade.Id, "NOT-EXISTS");

        Assert.False(success);
        Assert.Contains("رمز الاستبدال", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deactivate_rejects_self_as_replacement()
    {
        await using var db = CreateContext(AgencyTenantId);
        var grade = new ReferenceCatalogEntry
        {
            TenantId = AgencyTenantId,
            CatalogKind = CatalogKind.Grade,
            Code = "GR-99",
            NameAr = "درجة للتعطيل",
            IsActive = true
        };
        db.ReferenceCatalogEntries.Add(grade);
        await db.SaveChangesAsync();

        var service = new ReferenceCatalogAdminService(db);
        var (success, error) = await service.DeactivateAsync(grade.Id, "GR-99");

        Assert.False(success);
        Assert.Contains("نفسه", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deactivate_accepts_valid_replacement_code()
    {
        await using var db = CreateContext(AgencyTenantId);
        db.ReferenceCatalogEntries.AddRange(
            new ReferenceCatalogEntry
            {
                TenantId = AgencyTenantId,
                CatalogKind = CatalogKind.Grade,
                Code = "GR-OLD",
                NameAr = "قديم",
                IsActive = true
            },
            new ReferenceCatalogEntry
            {
                TenantId = AgencyTenantId,
                CatalogKind = CatalogKind.Grade,
                Code = "GR-NEW",
                NameAr = "جديد",
                IsActive = true
            });
        await db.SaveChangesAsync();

        var oldGrade = await db.ReferenceCatalogEntries.SingleAsync(e => e.Code == "GR-OLD");
        var service = new ReferenceCatalogAdminService(db);
        var (success, error) = await service.DeactivateAsync(oldGrade.Id, "GR-NEW");

        Assert.Null(error);
        Assert.True(success);
        var deactivated = await db.ReferenceCatalogEntries.SingleAsync(e => e.Id == oldGrade.Id);
        Assert.False(deactivated.IsActive);
        Assert.Equal("GR-NEW", deactivated.ReplacedByCode);
    }

    [Fact]
    public async Task List_merges_platform_with_agency_preferring_agency()
    {
        await using var db = CreateContext(AgencyTenantId);
        db.ReferenceCatalogEntries.AddRange(
            new ReferenceCatalogEntry
            {
                TenantId = TenantDefaults.DefaultTenantId,
                CatalogKind = CatalogKind.Grade,
                Code = "GR-MERGE",
                NameAr = "منصة",
                IsActive = true
            },
            new ReferenceCatalogEntry
            {
                TenantId = AgencyTenantId,
                CatalogKind = CatalogKind.Grade,
                Code = "GR-MERGE",
                NameAr = "جهة",
                IsActive = true
            });
        await db.SaveChangesAsync();

        var service = new ReferenceCatalogAdminService(db);
        var items = await service.ListAsync(CatalogKind.Grade, "GR-MERGE", null, 1, 20);

        Assert.Single(items);
        Assert.Equal("جهة", items[0].Entry.NameAr);
        Assert.True(items[0].CanMutate);
    }

    private static HRCoreDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new HRCoreDbContext(options, new FixedTenantContext(tenantId));
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public bool IsPlatformImpersonating => false;
        public Guid RequireTenantId() => tenantId;
    }
}
