using System.Text.Json;

namespace HR.Training.Domain.Services;

public record GapAnalysisResult(
    List<Guid> RequiredCourseIds,
    List<Guid> CompletedCourseIds,
    List<Guid> MissingCourseIds,
    decimal CompletionPercentage);

public static class TrainingGapAnalysis
{
    public static GapAnalysisResult AnalyzeGap(List<Guid> completedCourseIds, string? requiredCoursesJson)
    {
        var required = new List<Guid>();
        if (!string.IsNullOrWhiteSpace(requiredCoursesJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(requiredCoursesJson);
                if (parsed != null)
                {
                    foreach (var s in parsed)
                    {
                        if (Guid.TryParse(s, out var g))
                            required.Add(g);
                    }
                }
            }
            catch { }
        }

        if (required.Count == 0)
            return new GapAnalysisResult(required, [], [], 100);

        var completed = required.Where(completedCourseIds.Contains).ToList();
        var missing = required.Where(r => !completedCourseIds.Contains(r)).ToList();
        var pct = required.Count > 0 ? Math.Round((decimal)completed.Count / required.Count * 100, 1) : 100;

        return new GapAnalysisResult(required, completed, missing, pct);
    }
}
