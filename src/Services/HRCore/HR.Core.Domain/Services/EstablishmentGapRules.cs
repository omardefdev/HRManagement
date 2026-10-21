namespace HR.Core.Domain.Services;

public sealed record EstablishmentMetricsSnapshot(
    int FundedCount,
    decimal FundedFTE,
    int OccupiedCount,
    int VacantCount,
    int FrozenCount);

public sealed record EstablishmentGapLineResult(
    string? GradeCode,
    Guid? JobCatalogEntryId,
    int PlannedCount,
    decimal PlannedFTE,
    int FundedCount,
    decimal FundedFTE,
    int OccupiedCount,
    int VacantCount,
    int FrozenCount,
    int DeficitCount,
    int SurplusCount,
    decimal DeficitFTE,
    decimal SurplusFTE);

public sealed record EstablishmentGapSummary(
    int PlannedTotal,
    decimal PlannedFTETotal,
    EstablishmentMetricsSnapshot Actual,
    int DeficitCount,
    int SurplusCount,
    decimal DeficitFTE,
    decimal SurplusFTE,
    IReadOnlyList<EstablishmentGapLineResult> Lines);

/// <summary>EST-004: pure gap rules for establishment planning.</summary>
public static class EstablishmentGapRules
{
    public static (int Deficit, int Surplus) CompareCounts(int planned, int actual)
    {
        var delta = planned - actual;
        return (delta > 0 ? delta : 0, delta < 0 ? -delta : 0);
    }

    public static (decimal Deficit, decimal Surplus) CompareFte(decimal planned, decimal actual)
    {
        var delta = planned - actual;
        return (delta > 0 ? delta : 0, delta < 0 ? -delta : 0);
    }

    public static EstablishmentGapLineResult BuildLineResult(
        string? gradeCode,
        Guid? jobCatalogEntryId,
        int plannedCount,
        decimal plannedFte,
        EstablishmentMetricsSnapshot actual)
    {
        var (deficitCount, surplusCount) = CompareCounts(plannedCount, actual.FundedCount);
        var (deficitFte, surplusFte) = CompareFte(plannedFte, actual.FundedFTE);
        return new EstablishmentGapLineResult(
            gradeCode,
            jobCatalogEntryId,
            plannedCount,
            plannedFte,
            actual.FundedCount,
            actual.FundedFTE,
            actual.OccupiedCount,
            actual.VacantCount,
            actual.FrozenCount,
            deficitCount,
            surplusCount,
            deficitFte,
            surplusFte);
    }

    public static EstablishmentGapSummary BuildSummary(
        IReadOnlyList<EstablishmentGapLineResult> lines,
        EstablishmentMetricsSnapshot totalActual)
    {
        var plannedTotal = lines.Sum(l => l.PlannedCount);
        var plannedFteTotal = lines.Sum(l => l.PlannedFTE);
        var (deficitCount, surplusCount) = CompareCounts(plannedTotal, totalActual.FundedCount);
        var (deficitFte, surplusFte) = CompareFte(plannedFteTotal, totalActual.FundedFTE);
        return new EstablishmentGapSummary(
            plannedTotal,
            plannedFteTotal,
            totalActual,
            deficitCount,
            surplusCount,
            deficitFte,
            surplusFte,
            lines);
    }
}
