using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Establishment;

public interface IEstablishmentMetricsService
{
    Task<EstablishmentMetricsSnapshot> AggregateAsync(
        Guid orgUnitId,
        int budgetYear,
        DateTime? asOf = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<PositionMetricRow>> ListPositionRowsAsync(
        Guid orgUnitId,
        int budgetYear,
        DateTime? asOf = null,
        CancellationToken ct = default);
}

public sealed record PositionMetricRow(
    Guid FundedPositionId,
    string PositionCode,
    string GradeCode,
    Guid? JobCatalogEntryId,
    string? CostCenter,
    decimal FTE,
    FundedPositionStatus Status);

public sealed class EstablishmentMetricsService(
    HRCoreDbContext db,
    IFundedPositionVersionService fundedPositionVersions) : IEstablishmentMetricsService
{
    public async Task<EstablishmentMetricsSnapshot> AggregateAsync(
        Guid orgUnitId,
        int budgetYear,
        DateTime? asOf = null,
        CancellationToken ct = default)
    {
        var rows = await ListPositionRowsAsync(orgUnitId, budgetYear, asOf, ct);
        return ToSnapshot(rows);
    }

    public async Task<IReadOnlyList<PositionMetricRow>> ListPositionRowsAsync(
        Guid orgUnitId,
        int budgetYear,
        DateTime? asOf = null,
        CancellationToken ct = default)
    {
        var scopePath = await GetMaterializedPathAsync(orgUnitId, ct);
        var positionIds = await db.FundedPositions.AsNoTracking()
            .Where(p => p.BudgetYear == budgetYear)
            .Where(p => p.OrgUnit != null && p.OrgUnit.MaterializedPath.StartsWith(scopePath))
            .Select(p => new { p.Id, p.PositionCode, p.GradeCode, p.JobCatalogEntryId, p.CostCenter, p.FTE, p.Status })
            .ToListAsync(ct);

        if (positionIds.Count == 0)
            return [];

        if (asOf is null)
        {
            return positionIds
                .Select(p => new PositionMetricRow(
                    p.Id, p.PositionCode, p.GradeCode, p.JobCatalogEntryId, p.CostCenter, p.FTE, p.Status))
                .ToList();
        }

        var at = asOf.Value.ToUniversalTime();
        var rows = new List<PositionMetricRow>();
        foreach (var p in positionIds)
        {
            var version = await fundedPositionVersions.ResolveAsOfAsync(p.Id, at, ct);
            if (version is null) continue;
            rows.Add(new PositionMetricRow(
                version.FundedPositionId,
                version.PositionCode,
                version.GradeCode,
                version.JobCatalogEntryId,
                version.CostCenter,
                version.FTE,
                version.Status));
        }

        return rows;
    }

    internal static EstablishmentMetricsSnapshot ToSnapshot(IReadOnlyList<PositionMetricRow> rows) =>
        new(
            rows.Count,
            rows.Sum(r => r.FTE),
            rows.Count(r => r.Status == FundedPositionStatus.Occupied),
            rows.Count(r => r.Status == FundedPositionStatus.Vacant),
            rows.Count(r => r.Status == FundedPositionStatus.Frozen));

    private async Task<string> GetMaterializedPathAsync(Guid orgUnitId, CancellationToken ct)
    {
        var path = await db.OrgUnits.AsNoTracking()
            .Where(o => o.Id == orgUnitId)
            .Select(o => o.MaterializedPath)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrEmpty(path) ? $"/{orgUnitId:D}/" : path;
    }
}
