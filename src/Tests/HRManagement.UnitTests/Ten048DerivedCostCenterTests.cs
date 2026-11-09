using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-48 (§3, D-TEN-6, D-TEN-7) with the real FundedPositionCodeService and the seeded national policies: the cost centre is the
/// unit's derived code, shared by its positions, with no counter and one issuance; the position counter is per (unit, grade) across
/// budget years; a draft with no tenant is numbered in the session's tenant.
/// </summary>
public class Ten048DerivedCostCenterTests
{
    private static readonly Guid AgencyA = Guid.Parse("a0000001-0000-4000-8000-000000000001");

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }

    [Fact]
    public async Task Positions_of_one_unit_share_one_cost_centre_issued_once_and_count_across_budget_years()
    {
        var name = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(name).Options;
        var unit = new OrgUnit { TenantId = AgencyA, Code = "01.02", NameAr = "وحدة", NameEn = "Unit" };
        var other = new OrgUnit { TenantId = AgencyA, Code = "01.03", NameAr = "وحدة 2", NameEn = "Unit 2" };
        await using (var seed = new HRCoreDbContext(options))
        {
            await CodeGenerationSeeder.SeedAsync(seed);
            seed.OrgUnits.AddRange(unit, other);
            await seed.SaveChangesAsync();
        }

        await using var db = new HRCoreDbContext(options, new FixedTenant(AgencyA));
        var service = new FundedPositionCodeService(db, new CodeGeneratorService(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<CodeGeneratorService>.Instance));

        var first = await service.CreateAsync(new FundedPosition { OrgUnitId = unit.Id, GradeCode = "G5", BudgetYear = 2026, TenantId = AgencyA });
        // A draft built without a tenant (as EstablishmentChangeApplier does) is numbered in the session's tenant.
        var second = await service.CreateAsync(new FundedPosition { OrgUnitId = unit.Id, GradeCode = "G5", BudgetYear = 2026 });
        var nextYear = await service.CreateAsync(new FundedPosition { OrgUnitId = unit.Id, GradeCode = "G5", BudgetYear = 2027, TenantId = AgencyA });
        var elsewhere = await service.CreateAsync(new FundedPosition { OrgUnitId = other.Id, GradeCode = "G5", BudgetYear = 2026, TenantId = AgencyA });

        Assert.Equal(new[] { "01.02-G5-001", "01.02-G5-002", "01.02-G5-003", "01.03-G5-001" },
            new[] { first.PositionCode, second.PositionCode, nextYear.PositionCode, elsewhere.PositionCode });
        Assert.Equal(new[] { "01.02", "01.02", "01.02", "01.03" }, new[] { first.CostCenter, second.CostCenter, nextYear.CostCenter, elsewhere.CostCenter });

        await using var check = new HRCoreDbContext(options);
        var costCentres = await check.CodeIssuances.Where(i => i.EntityKind == CodeEntityKind.CostCenter).OrderBy(i => i.Code).ToListAsync();
        Assert.Equal(new[] { "01.02", "01.03" }, costCentres.Select(i => i.Code));
        Assert.All(costCentres, i => Assert.Equal(AgencyA, i.TenantId));
        Assert.False(await check.CodeSequenceCounters.AnyAsync(c => c.EntityKind == CodeEntityKind.CostCenter));
        Assert.Equal(4, await check.CodeIssuances.CountAsync(i => i.EntityKind == CodeEntityKind.FundedPosition && i.TenantId == AgencyA));
        Assert.False(await check.CodeIssuances.AnyAsync(i => i.TenantId == Guid.Empty));
    }
}
