using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Establishment;

public interface IEstablishmentGapAnalysisService
{
    Task<EstablishmentGapSummary?> AnalyzePlanAsync(
        Guid workforcePlanId,
        DateTime? asOf = null,
        CancellationToken ct = default);

    Task<EstablishmentGapSummary> AnalyzeOrgAsync(
        Guid orgUnitId,
        int fiscalYear,
        IReadOnlyList<WorkforcePlanLine> planLines,
        DateTime? asOf = null,
        CancellationToken ct = default);
}

public sealed class EstablishmentGapAnalysisService(
    HRCoreDbContext db,
    IEstablishmentMetricsService metricsService) : IEstablishmentGapAnalysisService
{
    public async Task<EstablishmentGapSummary?> AnalyzePlanAsync(
        Guid workforcePlanId,
        DateTime? asOf = null,
        CancellationToken ct = default)
    {
        var plan = await db.WorkforcePlans.AsNoTracking()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == workforcePlanId, ct);
        if (plan?.OrgUnitId is not Guid orgUnitId)
            return null;

        return await AnalyzeOrgAsync(orgUnitId, plan.FiscalYear, plan.Lines.ToList(), asOf, ct);
    }

    public async Task<EstablishmentGapSummary> AnalyzeOrgAsync(
        Guid orgUnitId,
        int fiscalYear,
        IReadOnlyList<WorkforcePlanLine> planLines,
        DateTime? asOf = null,
        CancellationToken ct = default)
    {
        var rows = await metricsService.ListPositionRowsAsync(orgUnitId, fiscalYear, asOf, ct);
        var totalActual = EstablishmentMetricsService.ToSnapshot(rows);

        var lineResults = new List<EstablishmentGapLineResult>();
        foreach (var line in planLines)
        {
            var matched = rows.Where(r => MatchesLine(r, line)).ToList();
            var actual = EstablishmentMetricsService.ToSnapshot(matched);
            lineResults.Add(EstablishmentGapRules.BuildLineResult(
                line.GradeCode,
                line.JobCatalogEntryId,
                line.PlannedCount,
                line.PlannedFTE,
                actual));
        }

        if (planLines.Count == 0)
            return EstablishmentGapRules.BuildSummary([], totalActual);

        return EstablishmentGapRules.BuildSummary(lineResults, totalActual);
    }

    private static bool MatchesLine(PositionMetricRow row, WorkforcePlanLine line)
    {
        if (line.JobCatalogEntryId.HasValue && row.JobCatalogEntryId != line.JobCatalogEntryId)
            return false;
        if (!string.IsNullOrWhiteSpace(line.GradeCode)
            && !string.Equals(row.GradeCode, line.GradeCode, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
