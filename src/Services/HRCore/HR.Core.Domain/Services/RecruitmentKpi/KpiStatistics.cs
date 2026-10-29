using System.Globalization;

namespace HR.Core.Domain.Services.RecruitmentKpi;

/// <summary>
/// HCM-032 (PLAN-32): the statistics every recruitment KPI uses (STR-007 definitions). Median of an even count is the mean
/// of the two middle values; P90 is nearest-rank (the value at position ceil(0.9 × n) in ascending order). Results are
/// rounded to one decimal (away from zero). An empty input or a zero denominator has no value, shown «—».
/// </summary>
public static class KpiStatistics
{
    public const string NoValue = "—";

    public static decimal? Median(IEnumerable<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return null;
        var mid = sorted.Count / 2;
        var median = sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
        return Round(median);
    }

    public static decimal? Median(IEnumerable<int> values) => Median(values.Select(v => (decimal)v));

    public static decimal? P90(IEnumerable<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return null;
        var position = (int)Math.Ceiling(0.9m * sorted.Count);
        return Round(sorted[Math.Max(position, 1) - 1]);
    }

    public static decimal? P90(IEnumerable<int> values) => P90(values.Select(v => (decimal)v));

    /// <summary>Percentage numerator ÷ denominator × 100, one decimal; null when the denominator is zero.</summary>
    public static decimal? Rate(int numerator, int denominator) =>
        denominator <= 0 ? null : Round(numerator * 100m / denominator);

    /// <summary>Share of a cohort (coverage), same rule as <see cref="Rate"/>.</summary>
    public static decimal? Coverage(int covered, int total) => Rate(covered, total);

    public static decimal Round(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    /// <summary>Invariant display ("12.5", "—"): never depends on the request culture.</summary>
    public static string Format(decimal? value) =>
        value is null ? NoValue : value.Value.ToString("0.#", CultureInfo.InvariantCulture);

    public static string FormatPercent(decimal? value) => value is null ? NoValue : Format(value) + "%";

    /// <summary>Calendar days between the UTC dates of two instants (DateOnly difference).</summary>
    public static int CalendarDays(DateTime fromUtc, DateTime toUtc) =>
        DateOnly.FromDateTime(toUtc).DayNumber - DateOnly.FromDateTime(fromUtc).DayNumber;
}
