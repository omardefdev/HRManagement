using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class Est004EstablishmentPlanningTests
{
    [Fact]
    public async Task Plan_10_planned_8_funded_deficit_2()
    {
        await using var db = CreateContext();
        var org = await SeedOrgAsync(db);
        for (var i = 0; i < 8; i++)
        {
            db.FundedPositions.Add(new FundedPosition
            {
                PositionCode = $"P-{i}",
                GradeCode = "G5",
                BudgetYear = 2026,
                Status = FundedPositionStatus.Vacant,
                OrgUnitId = org.Id
            });
        }

        var plan = new WorkforcePlan
        {
            Title = "Plan",
            FiscalYear = 2026,
            OrgUnitId = org.Id,
            Status = "approved",
            Lines = { new WorkforcePlanLine { GradeCode = "G5", PlannedCount = 10, PlannedFTE = 10 } }
        };
        db.WorkforcePlans.Add(plan);
        await db.SaveChangesAsync();

        var gap = new EstablishmentGapAnalysisService(db, new EstablishmentMetricsService(db, new FundedPositionVersionService(db)));
        var summary = await gap.AnalyzePlanAsync(plan.Id);

        Assert.NotNull(summary);
        Assert.Equal(10, summary!.PlannedTotal);
        Assert.Equal(8, summary.Actual.FundedCount);
        Assert.Equal(2, summary.DeficitCount);
    }

    [Fact]
    public async Task Metrics_occupied_vacant_frozen_counts()
    {
        await using var db = CreateContext();
        var org = await SeedOrgAsync(db);
        AddPosition(db, org.Id, FundedPositionStatus.Occupied, "G5");
        AddPosition(db, org.Id, FundedPositionStatus.Occupied, "G5");
        AddPosition(db, org.Id, FundedPositionStatus.Occupied, "G5");
        AddPosition(db, org.Id, FundedPositionStatus.Occupied, "G5");
        AddPosition(db, org.Id, FundedPositionStatus.Occupied, "G5");
        AddPosition(db, org.Id, FundedPositionStatus.Vacant, "G5");
        AddPosition(db, org.Id, FundedPositionStatus.Vacant, "G5");
        AddPosition(db, org.Id, FundedPositionStatus.Vacant, "G5");
        AddPosition(db, org.Id, FundedPositionStatus.Frozen, "G5");
        AddPosition(db, org.Id, FundedPositionStatus.Frozen, "G5");
        await db.SaveChangesAsync();

        var metrics = new EstablishmentMetricsService(db, new FundedPositionVersionService(db));
        var snap = await metrics.AggregateAsync(org.Id, 2026);

        Assert.Equal(10, snap.FundedCount);
        Assert.Equal(5, snap.OccupiedCount);
        Assert.Equal(3, snap.VacantCount);
        Assert.Equal(2, snap.FrozenCount);
    }

    [Fact]
    public async Task Csv_import_valid_rows()
    {
        var rows = FileFinanceBudgetPort.ParseCsvContent(
            "CostCenter,GradeCode,ApprovedCount,ApprovedFTE,BudgetAmount\nCC-01,G5,5,5,100000\nCC-02,,3,3,50000");
        Assert.Equal(2, rows.Count);
        Assert.Equal("CC-01", rows[0].CostCenter);
        Assert.Equal(5, rows[0].ApprovedCount);
    }

    [Fact]
    public async Task Reconciliation_cost_center_variance()
    {
        await using var db = CreateContext();
        var org = await SeedOrgAsync(db);
        for (var i = 0; i < 4; i++)
        {
            db.FundedPositions.Add(new FundedPosition
            {
                PositionCode = $"CC1-{i}",
                GradeCode = "G5",
                BudgetYear = 2026,
                Status = FundedPositionStatus.Vacant,
                OrgUnitId = org.Id,
                CostCenter = "CC-01"
            });
        }
        await db.SaveChangesAsync();

        var batch = new FinanceBudgetImportBatch
        {
            SourceFileName = "test.csv",
            FiscalYear = 2026,
            Status = FinanceImportStatus.Applied,
            RowCount = 1,
            Lines =
            {
                new FinanceBudgetLine { CostCenter = "CC-01", ApprovedCount = 5, ApprovedFTE = 5 }
            }
        };
        db.FinanceBudgetImportBatches.Add(batch);
        await db.SaveChangesAsync();

        var recon = new EstablishmentFinanceReconciliationService(db, new EstablishmentMetricsService(db, new FundedPositionVersionService(db)));
        var result = await recon.ReconcileAsync(2026, batch.Id, org.Id);

        var row = Assert.Single(result.Rows);
        Assert.Equal("CC-01", row.CostCenter);
        Assert.Equal(5, row.ImportedCount);
        Assert.Equal(4, row.ActualFundedCount);
        Assert.Equal(1, row.CountVariance);
    }

    [Fact]
    public void Gap_rules_deficit_and_surplus()
    {
        var (deficit, surplus) = EstablishmentGapRules.CompareCounts(10, 8);
        Assert.Equal(2, deficit);
        Assert.Equal(0, surplus);

        (deficit, surplus) = EstablishmentGapRules.CompareCounts(8, 10);
        Assert.Equal(0, deficit);
        Assert.Equal(2, surplus);
    }

    [Fact]
    public async Task AsOf_before_freeze_frozen_count_zero()
    {
        await using var db = CreateContext();
        var org = await SeedOrgAsync(db);
        var freezeAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var position = new FundedPosition
        {
            PositionCode = "FRZ-001",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Frozen,
            OrgUnitId = org.Id,
            FreezeDate = freezeAt,
            FreezeReason = "تجميد اختباري"
        };
        db.FundedPositions.Add(position);
        await db.SaveChangesAsync();

        db.FundedPositionVersions.Add(new FundedPositionVersion
        {
            FundedPositionId = position.Id,
            ValidFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidTo = freezeAt,
            PositionCode = position.PositionCode,
            GradeCode = position.GradeCode,
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id,
            FTE = 1m
        });
        db.FundedPositionVersions.Add(new FundedPositionVersion
        {
            FundedPositionId = position.Id,
            ValidFrom = freezeAt,
            ValidTo = null,
            PositionCode = position.PositionCode,
            GradeCode = position.GradeCode,
            BudgetYear = 2026,
            Status = FundedPositionStatus.Frozen,
            OrgUnitId = org.Id,
            FTE = 1m,
            FreezeReason = position.FreezeReason
        });
        await db.SaveChangesAsync();

        var metrics = new EstablishmentMetricsService(db, new FundedPositionVersionService(db));
        var before = await metrics.AggregateAsync(org.Id, 2026, freezeAt.AddDays(-1));
        var after = await metrics.AggregateAsync(org.Id, 2026, freezeAt.AddDays(1));

        Assert.Equal(1, before.FundedCount);
        Assert.Equal(0, before.FrozenCount);
        Assert.Equal(1, after.FundedCount);
        Assert.Equal(1, after.FrozenCount);
    }

    [Fact]
    public async Task Gap_does_not_use_analytics()
    {
        await using var db = CreateContext();
        Assert.DoesNotContain(db.GetType().GetProperties(), p => p.Name == "AnalyticsEvents");

        var org = await SeedOrgAsync(db);
        db.FundedPositions.Add(new FundedPosition
        {
            PositionCode = "ANA-001",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        });
        var plan = new WorkforcePlan
        {
            Title = "Analytics isolation",
            FiscalYear = 2026,
            OrgUnitId = org.Id,
            Status = "approved",
            Lines = { new WorkforcePlanLine { GradeCode = "G5", PlannedCount = 2, PlannedFTE = 2 } }
        };
        db.WorkforcePlans.Add(plan);
        await db.SaveChangesAsync();

        var gap = new EstablishmentGapAnalysisService(db, new EstablishmentMetricsService(db, new FundedPositionVersionService(db)));
        var summary = await gap.AnalyzePlanAsync(plan.Id);

        Assert.NotNull(summary);
        Assert.Equal(1, summary!.Actual.FundedCount);
        Assert.Equal(1, summary.DeficitCount);
    }

    [Fact]
    public async Task Csv_import_rejects_invalid_rows()
    {
        await using var db = CreateContext();
        var service = new FinanceBudgetImportService(db, new FileFinanceBudgetPort());
        var csv = "CostCenter,GradeCode,ApprovedCount,ApprovedFTE,BudgetAmount\nCC-01,G5,-1,1,1000\n";
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        var (batch, error) = await service.ImportCsvAsync(stream, "bad.csv", 2026, null);

        Assert.Null(batch);
        Assert.NotNull(error);
        Assert.Contains("غير صالحة", error, StringComparison.Ordinal);
    }

    private static void AddPosition(HRCoreDbContext db, Guid orgId, FundedPositionStatus status, string grade)
    {
        db.FundedPositions.Add(new FundedPosition
        {
            PositionCode = Guid.NewGuid().ToString("N")[..8],
            GradeCode = grade,
            BudgetYear = 2026,
            Status = status,
            OrgUnitId = orgId
        });
    }

    private static async Task<OrgUnit> SeedOrgAsync(HRCoreDbContext db)
    {
        var org = new OrgUnit { Code = "OU-P", NameAr = "فرع", NameEn = "Branch", MaterializedPath = "/ou/" };
        db.OrgUnits.Add(org);
        await db.SaveChangesAsync();
        org.MaterializedPath = $"/{org.Id:D}/";
        await db.SaveChangesAsync();
        return org;
    }

    private static HRCoreDbContext CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new HRCoreDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
