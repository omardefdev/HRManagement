using HR.Core.Domain.Recruitment;

namespace HR.Core.Domain.Services;

/// <summary>
/// HCM-026: weighted scoring. Per assessor: Σ(score / MaxScore × Weight).
/// An assessor is complete when every mandatory item is scored.
/// Final score = mean of complete assessors, rounded to 2 decimals (AwayFromZero).
/// </summary>
public static class AssessmentScoringRules
{
    public const string FormulaDescription =
        "درجة المقيّم = Σ (الدرجة ÷ الحد الأعلى × الوزن)؛ الدرجة النهائية = متوسط درجات المقيّمين المكتملين (مقرّبة إلى منزلتين).";

    public sealed record AssessorScore(string ItemKey, decimal Score);

    public sealed record AssessorResult(
        Guid AssessorUserId,
        decimal WeightedTotal,
        bool IsComplete,
        int ScoredItems);

    public static AssessorResult ComputeAssessor(
        Guid assessorUserId,
        IReadOnlyList<AssessmentCriteriaItem> items,
        IEnumerable<AssessorScore> scores)
    {
        var (total, complete, count) = Evaluate(items, scores);
        return new AssessorResult(assessorUserId, Round(total), complete, count);
    }

    /// <summary>Mean of the complete assessors' unrounded totals, or null when none is complete.</summary>
    public static decimal? ComputeFinal(
        IReadOnlyList<AssessmentCriteriaItem> items,
        IEnumerable<IEnumerable<AssessorScore>> scoresPerAssessor)
    {
        var totals = scoresPerAssessor
            .Select(scores => Evaluate(items, scores))
            .Where(r => r.Complete)
            .Select(r => r.Total)
            .ToList();

        return totals.Count == 0 ? null : Round(totals.Average());
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static (decimal Total, bool Complete, int Count) Evaluate(
        IReadOnlyList<AssessmentCriteriaItem> items,
        IEnumerable<AssessorScore> scores)
    {
        var byKey = items.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);
        var scored = scores
            .Where(s => byKey.ContainsKey(s.ItemKey))
            .GroupBy(s => s.ItemKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Score, StringComparer.OrdinalIgnoreCase);

        var total = scored.Sum(s => s.Value / byKey[s.Key].MaxScore * byKey[s.Key].Weight);
        var complete = items.Where(i => i.IsMandatory).All(i => scored.ContainsKey(i.Key));
        return (total, complete, scored.Count);
    }
}
