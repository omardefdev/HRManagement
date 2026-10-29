using System.Globalization;

namespace HR.Core.Domain.Services.RecruitmentKpi;

/// <summary>
/// HCM-032 (PLAN-32, decision 7): small-cell suppression (STR-007 <c>privacyRules.minCellSize</c> = 5). A count from 1 to
/// threshold − 1 is shown «&lt;5»; zero and counts at or above the threshold are shown as is. A rate whose numerator or
/// denominator is suppressed is suppressed too, so a hidden count cannot be derived back.
/// </summary>
public static class SmallCellSuppression
{
    public const int DefaultThreshold = 5;

    public static int Effective(int threshold) => threshold > 0 ? threshold : DefaultThreshold;

    public static bool IsSuppressed(int count, int threshold) => count > 0 && count < Effective(threshold);

    public static string Label(int threshold) => "<" + Effective(threshold).ToString(CultureInfo.InvariantCulture);

    public static string Display(int count, int threshold) =>
        IsSuppressed(count, threshold) ? Label(threshold) : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Null when the count is suppressed (for published payloads).</summary>
    public static int? Value(int count, int threshold) => IsSuppressed(count, threshold) ? null : count;

    public static string DisplayRate(int numerator, int denominator, int threshold) =>
        IsSuppressed(numerator, threshold) || IsSuppressed(denominator, threshold)
            ? Label(threshold)
            : KpiStatistics.FormatPercent(KpiStatistics.Rate(numerator, denominator));
}
