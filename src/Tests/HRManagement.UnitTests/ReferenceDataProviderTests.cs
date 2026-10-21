using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.MasterData;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class ReferenceDataProviderTests
{
    private static readonly Guid AgencyTenantId = Guid.Parse("a0000001-0000-4000-8000-000000000001");

    [Fact]
    public async Task SearchActiveAsync_falls_back_to_platform_mdm_when_agency_has_no_copy()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seedDb = CreateDb(TenantDefaults.DefaultTenantId, dbName))
        {
            seedDb.ReferenceCatalogEntries.Add(new ReferenceCatalogEntry
            {
                TenantId = TenantDefaults.DefaultTenantId,
                CatalogKind = CatalogKind.Grade,
                Code = "GR-05",
                NameAr = "الدرجة الخامسة",
                IsActive = true
            });
            await seedDb.SaveChangesAsync();
        }

        await using var agencyDb = CreateDb(AgencyTenantId, dbName);
        var provider = new ReferenceDataProvider(agencyDb);

        var items = await provider.SearchActiveAsync(CatalogKind.Grade, query: null, skip: 0, take: 10);

        Assert.Contains(items, e => e.Code == "GR-05");
    }

    [Fact]
    public async Task GetByCodeAsync_prefers_agency_copy_over_platform()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seedDb = CreateDb(TenantDefaults.DefaultTenantId, dbName))
        {
            seedDb.ReferenceCatalogEntries.AddRange(
                new ReferenceCatalogEntry
                {
                    TenantId = TenantDefaults.DefaultTenantId,
                    CatalogKind = CatalogKind.Grade,
                    Code = "GR-05",
                    NameAr = "الدرجة الخامسة",
                    IsActive = true
                },
                new ReferenceCatalogEntry
                {
                    TenantId = AgencyTenantId,
                    CatalogKind = CatalogKind.Grade,
                    Code = "GR-05",
                    NameAr = "درجة مخصصة للجهة",
                    IsActive = true
                });
            await seedDb.SaveChangesAsync();
        }

        await using var agencyDb = CreateDb(AgencyTenantId, dbName);
        var provider = new ReferenceDataProvider(agencyDb);

        var entry = await provider.GetByCodeAsync(CatalogKind.Grade, "GR-05");

        Assert.NotNull(entry);
        Assert.Equal("درجة مخصصة للجهة", entry!.NameAr);
    }

    private static HRCoreDbContext CreateDb(Guid tenantId, string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
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
