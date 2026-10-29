using System.Globalization;
using Dev.Analytics.Domain.Entities;

namespace Dev.Analytics.Domain.Services;

public sealed record RecruitmentCardRow(string Label, string Value);

/// <summary>«التوظيف» card of the Dev.Analytics dashboard (recruitment requisitions closed in the last 12 months).</summary>
public sealed record RecruitmentCard(
    int RequisitionsClosed,
    int RequisitionsCancelled,
    string Filled,
    string MedianTimeToFillDays,
    IReadOnlyList<RecruitmentCardRow> Channels,
    IReadOnlyList<RecruitmentCardRow> OfferOutcomes);

/// <summary>
/// HCM-032 (PLAN-32, P6, decision 7): the recruitment aggregates published outside HR.Core. Small cells (1 to
/// <see cref="MinCellSize"/> − 1; STR-007 <c>privacyRules.minCellSize</c>) are shown «&lt;5» in the channel and offer
/// outcome breakdowns, and the filled count when it is small. Exact counts (no sampling).
/// </summary>
public static class RecruitmentFactRules
{
    public const int MinCellSize = 5;

    public static string Cell(int count) =>
        count > 0 && count < MinCellSize ? "<" + MinCellSize.ToString(CultureInfo.InvariantCulture) : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Median of an even count = mean of the two middle values, one decimal (as HR.Core's STR-007 rule).</summary>
    public static string Median(IEnumerable<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return "—";
        var mid = sorted.Count / 2;
        var median = sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
        return Math.Round(median, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);
    }

    public static RecruitmentCard BuildCard(IReadOnlyCollection<RecruitmentRequisitionFact> facts)
    {
        var closed = facts.Where(f => f.Outcome != "Cancelled").ToList();
        var filled = closed.Count(f => f.HiredCount > 0);
        return new RecruitmentCard(
            closed.Count,
            facts.Count - closed.Count,
            Cell(filled),
            // The median of fewer than MinCellSize filled requisitions is suppressed with them.
            filled is > 0 and < MinCellSize ? "<" + MinCellSize.ToString(CultureInfo.InvariantCulture)
                : Median(closed.Where(f => f.TimeToFillDays.HasValue).Select(f => f.TimeToFillDays!.Value)),
            [
                new("عام", Cell(facts.Sum(f => f.ApplicantsPublic))),
                new("داخلي", Cell(facts.Sum(f => f.ApplicantsInternal))),
                new("إدخال يدوي", Cell(facts.Sum(f => f.ApplicantsHrEntered))),
                new("قديم", Cell(facts.Sum(f => f.ApplicantsLegacy)))
            ],
            [
                new("مقبول", Cell(facts.Sum(f => f.OffersAccepted))),
                new("مرفوض", Cell(facts.Sum(f => f.OffersDeclined))),
                new("منتهٍ", Cell(facts.Sum(f => f.OffersExpired))),
                new("ملغى", Cell(facts.Sum(f => f.OffersRevoked)))
            ]);
    }
}
