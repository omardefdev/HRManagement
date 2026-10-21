using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>
/// PLAN-17 AC-EST-10..12: approved BR1 workforce plan + finance budget batch with variance.
/// Expected BR1 metrics (after full acceptance seed): funded = occupied + vacant + frozen;
/// plan G5 line uses funded + 2 for a positive deficit in gap analysis.
/// </summary>
public static class AcceptancePlanningPatch
{
    private static readonly string[] SampleCostCenters =
    [
        AcceptanceSeedIds.Br1SampleCostCenter,
        "CC-BR1-02",
        "CC-BR1-03"
    ];

    public static async Task ApplyAsync(HRCoreDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        TenantIdFlow.Set(AcceptanceSeedIds.AgencyAId);
        try
        {
            var br1Path = await db.OrgUnits.AsNoTracking()
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                .Where(o => o.Id == AcceptanceSeedIds.Br1OrgUnitId)
                .Select(o => o.MaterializedPath)
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(br1Path))
                return;

            var fiscalYear = AcceptanceSeedCatalog.ReferenceDate.Year;
            var br1Positions = await db.FundedPositions
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                .Include(p => p.OrgUnit)
                .Where(p => p.TenantId == AcceptanceSeedIds.AgencyAId
                    && p.BudgetYear == fiscalYear
                    && p.OrgUnit != null
                    && p.OrgUnit.MaterializedPath.StartsWith(br1Path))
                .OrderBy(p => p.PositionCode)
                .ToListAsync(ct);
            if (br1Positions.Count == 0)
                return;

            for (var i = 0; i < Math.Min(SampleCostCenters.Length, br1Positions.Count); i++)
                br1Positions[i].CostCenter = SampleCostCenters[i];

            var fundedCount = br1Positions.Count;
            var frozenCount = br1Positions.Count(p => p.Status == FundedPositionStatus.Frozen);
            var plannedCount = fundedCount + 2;

            await EnsureWorkforcePlanAsync(db, fiscalYear, fundedCount, frozenCount, plannedCount, logger, ct);

            if (!await db.FinanceBudgetImportBatches
                    .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                    .AnyAsync(b => b.Id == AcceptanceSeedIds.Br1FinanceBudgetBatchId, ct))
            {
                var costCenterCounts = br1Positions
                    .Where(p => !string.IsNullOrWhiteSpace(p.CostCenter))
                    .GroupBy(p => p.CostCenter!, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .Select(g => (CostCenter: g.Key, Actual: g.Count()))
                    .ToList();

                var batch = new FinanceBudgetImportBatch
                {
                    Id = AcceptanceSeedIds.Br1FinanceBudgetBatchId,
                    TenantId = AcceptanceSeedIds.AgencyAId,
                    SourceFileName = "br1-budget-sample.csv",
                    FiscalYear = fiscalYear,
                    ImportedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-7),
                    Status = FinanceImportStatus.Applied,
                    RowCount = costCenterCounts.Count,
                    Lines = costCenterCounts.Select(row => new FinanceBudgetLine
                    {
                        CostCenter = row.CostCenter,
                        GradeCode = "G5",
                        ApprovedCount = row.Actual + 2,
                        ApprovedFTE = row.Actual + 2,
                        BudgetAmount = (row.Actual + 2) * 100_000m
                    }).ToList()
                };
                db.FinanceBudgetImportBatches.Add(batch);
                logger?.LogInformation(
                    "Seeded BR1 finance batch with {Rows} cost-center lines (variance +2 each).",
                    batch.Lines.Count);
            }

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        }
    }

    private static async Task EnsureWorkforcePlanAsync(
        HRCoreDbContext db,
        int fiscalYear,
        int fundedCount,
        int frozenCount,
        int plannedCount,
        ILogger? logger,
        CancellationToken ct)
    {
        var plan = await db.WorkforcePlans
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == AcceptanceSeedIds.Br1WorkforcePlanId, ct);

        if (plan is null)
        {
            plan = new WorkforcePlan
            {
                Id = AcceptanceSeedIds.Br1WorkforcePlanId,
                TenantId = AcceptanceSeedIds.AgencyAId,
                Title = "خطة قوى BR1 — قبول EST-D",
                FiscalYear = fiscalYear,
                OrgUnitId = AcceptanceSeedIds.Br1OrgUnitId,
                Status = "approved",
                ApprovedAt = AcceptanceSeedCatalog.ReferenceDate.AddMonths(-1),
                LegalSourceId = "ACCEPT-EST-D-BR1"
            };
            db.WorkforcePlans.Add(plan);
            logger?.LogInformation("Creating BR1 workforce plan for AC-EST-10.");
        }

        var line = plan.Lines.FirstOrDefault();
        var needsSync = plan.Lines.Count != 1
            || line is null
            || line.PlannedCount != plannedCount
            || !string.IsNullOrWhiteSpace(line.GradeCode);

        if (needsSync)
        {
            await db.WorkforcePlanLines
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                .Where(l => l.WorkforcePlanId == AcceptanceSeedIds.Br1WorkforcePlanId)
                .ExecuteDeleteAsync(ct);

            db.WorkforcePlanLines.Add(new WorkforcePlanLine
            {
                WorkforcePlanId = plan.Id,
                TenantId = AcceptanceSeedIds.AgencyAId,
                PlannedCount = plannedCount,
                PlannedFTE = plannedCount,
                Notes = $"هدف subtree BR1 — عجز +2 (funded={fundedCount}, frozen={frozenCount})"
            });
            logger?.LogInformation(
                "Synced BR1 workforce plan: funded={Funded}, frozen={Frozen}, planned={Planned}",
                fundedCount, frozenCount, plannedCount);
        }
    }
}
