using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>PLAN-04 AC-14: BR1 head + funded position for temporal org acceptance.</summary>
public static class AcceptanceTemporalPatch
{
    public static async Task ApplyAsync(HRCoreDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        TenantIdFlow.Set(AcceptanceSeedIds.AgencyAId);
        try
        {
            var br1 = await db.OrgUnits
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                .FirstOrDefaultAsync(o => o.Id == AcceptanceSeedIds.Br1OrgUnitId, ct);
            if (br1 is null) return;

            if (!br1.HeadEmployeeId.HasValue)
            {
                br1.HeadEmployeeId = AcceptanceSeedIds.EmpBr1Id;
                logger?.LogInformation("Patched BR1 HeadEmployeeId for AC-14.");
            }

            var hasPosition = await db.FundedPositions
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                .AnyAsync(p => p.Id == AcceptanceSeedIds.Br1FundedPositionId
                    || p.OrgUnitId == AcceptanceSeedIds.Br1OrgUnitId, ct);
            if (!hasPosition)
            {
                db.FundedPositions.Add(new FundedPosition
                {
                    Id = AcceptanceSeedIds.Br1FundedPositionId,
                    TenantId = AcceptanceSeedIds.AgencyAId,
                    OrgUnitId = AcceptanceSeedIds.Br1OrgUnitId,
                    PositionCode = "POS-BR1-001",
                    GradeCode = "G5",
                    BudgetYear = AcceptanceSeedCatalog.ReferenceDate.Year,
                    Status = FundedPositionStatus.Occupied,
                    OccupantEmployeeId = AcceptanceSeedIds.EmpBr1Id,
                    CreatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)
                });
                logger?.LogInformation("Seeded BR1 funded position for AC-14.");
            }

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        }
    }
}
