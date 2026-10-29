using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-031: what the results computation needs about one admitted candidate.</summary>
/// <param name="ExamMarks">The closed exam mark and maximum of every exam type the candidate sat.</param>
/// <param name="NonExamPoints">Points of the non-exam stages (null = assessment incomplete; 0 when the criteria have none).</param>
/// <param name="EffectivePassMark">The lowest pass mark of the candidate's preferred lines (J24).</param>
public sealed record CompetitionScoreInput(
    Guid ApplicationId,
    string ExamNumber,
    bool Absent,
    IReadOnlyList<(decimal Mark, decimal MaxScore)> ExamMarks,
    decimal? NonExamPoints,
    decimal? InterviewPoints,
    decimal? YearsOfExperience,
    DateTime AppliedAt,
    decimal EffectivePassMark);

/// <summary>HCM-031: one computed result.</summary>
public sealed record CompetitionScored(
    Guid ApplicationId,
    string ExamNumber,
    decimal? ExamScore,
    decimal? InterviewScore,
    decimal? FinalScore,
    int? Rank,
    CompetitionResultOutcome Outcome,
    bool TieUnresolved,
    decimal? YearsOfExperience,
    DateTime AppliedAt);

/// <summary>HCM-031: a rank or outcome change between two results versions.</summary>
public sealed record CompetitionResultChange(string ExamNumber, int? FromRank, int? ToRank, string FromOutcome, string ToOutcome);

/// <summary>
/// HCM-031 (PLAN-31, AC-HCM-31d): pure results computation. Final score = exam points (the exam mark normalized to the
/// exam stage weight; the average over exam types) + the non-exam stage points (PLAN-26 <see cref="AssessmentScoringRules"/>).
/// Present candidates are ranked by final score, then the competition's tie-break order; a tie that remains gets the
/// same rank and is flagged («تعادل يتطلب قراراً») — never silently ordered.
/// </summary>
public static class CompetitionScoringRules
{
    public const string UnresolvedTieMessage = "تعادل يتطلب قراراً";
    public const int MinTieDecisionReasonLength = 10;

    public static decimal ExamPoints(decimal examWeight, IReadOnlyList<(decimal Mark, decimal MaxScore)> marks) =>
        marks.Count == 0 ? 0m : Round(marks.Average(m => m.Mark / m.MaxScore) * examWeight);

    public static IReadOnlyList<CompetitionScored> Compute(
        IReadOnlyList<CompetitionScoreInput> candidates,
        decimal examWeight,
        IReadOnlyList<CompetitionTieBreak> tieBreakOrder)
    {
        var scored = candidates.Select(c =>
        {
            if (c.Absent)
                return new CompetitionScored(c.ApplicationId, c.ExamNumber, null, null, null, null, CompetitionResultOutcome.Absent,
                    false, c.YearsOfExperience, c.AppliedAt);
            var exam = ExamPoints(examWeight, c.ExamMarks);
            var final = Round(exam + (c.NonExamPoints ?? 0m));
            var outcome = final >= c.EffectivePassMark ? CompetitionResultOutcome.Passed : CompetitionResultOutcome.Failed;
            return new CompetitionScored(c.ApplicationId, c.ExamNumber, exam, c.InterviewPoints, final, null, outcome, false,
                c.YearsOfExperience, c.AppliedAt);
        }).ToList();

        var comparer = Comparer(tieBreakOrder);
        var present = scored.Where(s => s.Outcome != CompetitionResultOutcome.Absent).OrderBy(s => s, comparer).ThenBy(s => s.ExamNumber, StringComparer.Ordinal).ToList();
        var ranked = new List<CompetitionScored>(scored.Count);
        var i = 0;
        while (i < present.Count)
        {
            var j = i + 1;
            while (j < present.Count && comparer.Compare(present[i], present[j]) == 0)
                j++;
            var tied = j - i > 1;
            for (var k = i; k < j; k++)
                ranked.Add(present[k] with { Rank = i + 1, TieUnresolved = tied });
            i = j;
        }
        ranked.AddRange(scored.Where(s => s.Outcome == CompetitionResultOutcome.Absent));
        return ranked;
    }

    /// <summary>Orders final score descending, then each tie-break (scores and experience descending, application date ascending).</summary>
    public static IComparer<CompetitionScored> Comparer(IReadOnlyList<CompetitionTieBreak> order) =>
        Comparer<CompetitionScored>.Create((a, b) =>
        {
            var c = Nullable.Compare(b.FinalScore, a.FinalScore);
            if (c != 0)
                return c;
            foreach (var tieBreak in order)
            {
                c = tieBreak switch
                {
                    CompetitionTieBreak.ExamScore => Nullable.Compare(b.ExamScore, a.ExamScore),
                    CompetitionTieBreak.InterviewScore => Nullable.Compare(b.InterviewScore, a.InterviewScore),
                    CompetitionTieBreak.YearsOfExperience => Nullable.Compare(b.YearsOfExperience, a.YearsOfExperience),
                    CompetitionTieBreak.AppliedAt => a.AppliedAt.CompareTo(b.AppliedAt),
                    _ => 0
                };
                if (c != 0)
                    return c;
            }
            return 0;
        });

    /// <summary>
    /// HR decision on one unresolved tie: the tied group (same rank, flagged) is ordered as given and gets consecutive
    /// ranks starting at the group's rank. Returns an error (Arabic) when the ids are not exactly one tied group.
    /// </summary>
    public static string? ValidateTieDecision(IReadOnlyList<(Guid ApplicationId, int? Rank, bool TieUnresolved)> results, IReadOnlyList<Guid> orderedIds, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < MinTieDecisionReasonLength)
            return $"سبب قرار التعادل مطلوب ({MinTieDecisionReasonLength} أحرف على الأقل).";
        var group = results.Where(r => orderedIds.Contains(r.ApplicationId)).ToList();
        if (group.Count != orderedIds.Count || orderedIds.Distinct().Count() != orderedIds.Count)
            return "قائمة التعادل غير صالحة.";
        if (group.Any(r => !r.TieUnresolved) || group.Select(r => r.Rank).Distinct().Count() != 1)
            return "المتقدمون المختارون ليسوا مجموعة تعادل واحدة.";
        var rank = group[0].Rank;
        if (results.Count(r => r.Rank == rank && r.TieUnresolved) != orderedIds.Count)
            return "يجب ترتيب كل أفراد مجموعة التعادل.";
        return null;
    }

    public static string CanonicalJson(int versionNo, IEnumerable<CompetitionResult> results)
    {
        var rows = results
            .OrderBy(r => r.ExamNumber, StringComparer.Ordinal)
            .Select(r => new SortedDictionary<string, string?>(StringComparer.Ordinal)
            {
                ["examNumber"] = r.ExamNumber,
                ["finalScore"] = r.FinalScore?.ToString("0.00", CultureInfo.InvariantCulture),
                ["outcome"] = r.Outcome.ToString(),
                ["rank"] = r.OverallRank?.ToString(CultureInfo.InvariantCulture)
            })
            .ToList();
        return JsonSerializer.Serialize(new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["results"] = rows,
            ["schema"] = "competition-results/1",
            ["version"] = versionNo
        });
    }

    public static string ComputeSha256(int versionNo, IEnumerable<CompetitionResult> results) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson(versionNo, results)))).ToLowerInvariant();

    /// <summary>Rank and outcome changes from <paramref name="previous"/> to <paramref name="current"/>, by exam number.</summary>
    public static IReadOnlyList<CompetitionResultChange> Diff(IEnumerable<CompetitionResult> previous, IEnumerable<CompetitionResult> current)
    {
        var before = previous.ToDictionary(r => r.ExamNumber, StringComparer.Ordinal);
        return current
            .OrderBy(r => r.ExamNumber, StringComparer.Ordinal)
            .Where(r => !before.TryGetValue(r.ExamNumber, out var b) || b.OverallRank != r.OverallRank || b.Outcome != r.Outcome)
            .Select(r => before.TryGetValue(r.ExamNumber, out var b)
                ? new CompetitionResultChange(r.ExamNumber, b.OverallRank, r.OverallRank, b.Outcome.ToString(), r.Outcome.ToString())
                : new CompetitionResultChange(r.ExamNumber, null, r.OverallRank, "—", r.Outcome.ToString()))
            .ToList();
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
