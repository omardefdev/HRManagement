using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Establishment;

public sealed record CostCenterReconciliationRow(
    string CostCenter,
    int ImportedCount,
    decimal ImportedFTE,
    int ActualFundedCount,
    decimal ActualFundedFTE,
    int CountVariance,
    decimal FteVariance);

public sealed record EstablishmentFinanceReconciliationResult(
    int FiscalYear,
    Guid? BatchId,
    IReadOnlyList<CostCenterReconciliationRow> Rows);

public interface IEstablishmentFinanceReconciliationService
{
    Task<EstablishmentFinanceReconciliationResult> ReconcileAsync(
        int fiscalYear,
        Guid? batchId = null,
        Guid? orgUnitId = null,
        CancellationToken ct = default);
}

public sealed class EstablishmentFinanceReconciliationService(
    HRCoreDbContext db,
    IEstablishmentMetricsService metricsService) : IEstablishmentFinanceReconciliationService
{
    public async Task<EstablishmentFinanceReconciliationResult> ReconcileAsync(
        int fiscalYear,
        Guid? batchId = null,
        Guid? orgUnitId = null,
        CancellationToken ct = default)
    {
        var batchQuery = db.FinanceBudgetImportBatches.AsNoTracking().Include(b => b.Lines).AsQueryable();
        if (batchId.HasValue)
            batchQuery = batchQuery.Where(b => b.Id == batchId);
        else
            batchQuery = batchQuery.Where(b => b.FiscalYear == fiscalYear && b.Status == Domain.Entities.FinanceImportStatus.Applied);

        var batch = await batchQuery.OrderByDescending(b => b.ImportedAt).FirstOrDefaultAsync(ct);
        var importedByCostCenter = batch?.Lines
            .GroupBy(l => l.CostCenter, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (Count: g.Sum(x => x.ApprovedCount), FTE: g.Sum(x => x.ApprovedFTE)),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, (int Count, decimal FTE)>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<PositionMetricRow> actualRows;
        if (orgUnitId.HasValue)
            actualRows = await metricsService.ListPositionRowsAsync(orgUnitId.Value, fiscalYear, null, ct);
        else
        {
            var hq = await db.OrgUnits.AsNoTracking().Where(o => o.ParentId == null).Select(o => o.Id).FirstOrDefaultAsync(ct);
            actualRows = hq == Guid.Empty
                ? []
                : await metricsService.ListPositionRowsAsync(hq, fiscalYear, null, ct);
        }

        var actualByCostCenter = actualRows
            .Where(r => !string.IsNullOrWhiteSpace(r.CostCenter))
            .GroupBy(r => r.CostCenter!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (Count: g.Count(), FTE: g.Sum(x => x.FTE)),
                StringComparer.OrdinalIgnoreCase);

        var allKeys = importedByCostCenter.Keys
            .Union(actualByCostCenter.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = allKeys.Select(key =>
        {
            importedByCostCenter.TryGetValue(key, out var imported);
            actualByCostCenter.TryGetValue(key, out var actual);
            return new CostCenterReconciliationRow(
                key,
                imported.Count,
                imported.FTE,
                actual.Count,
                actual.FTE,
                imported.Count - actual.Count,
                imported.FTE - actual.FTE);
        }).ToList();

        return new EstablishmentFinanceReconciliationResult(fiscalYear, batch?.Id, rows);
    }
}
