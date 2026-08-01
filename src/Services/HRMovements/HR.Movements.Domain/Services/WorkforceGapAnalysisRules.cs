using System.Text.Json;

namespace HR.Movements.Domain.Services;

public record PositionPlanEntry(string? Title, int Count);

public record WorkforceGapResult(
    int PlannedTotal,
    int ActualTotal,
    int Deficit,
    int Surplus,
    IReadOnlyList<PositionPlanEntry> PlannedBreakdown);

/// <summary>
/// Compares distribution plan headcount targets (P10.1) against actual workforce size.
/// </summary>
public static class WorkforceGapAnalysisRules
{
    public static IReadOnlyList<PositionPlanEntry> ParsePlannedPositions(string? plannedPositionsJson)
    {
        var entries = new List<PositionPlanEntry>();
        if (string.IsNullOrWhiteSpace(plannedPositionsJson))
            return entries;

        try
        {
            using var doc = JsonDocument.Parse(plannedPositionsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
                    {
                        entries.Add(new PositionPlanEntry(null, n));
                        continue;
                    }

                    if (el.ValueKind != JsonValueKind.Object)
                        continue;

                    var title = el.TryGetProperty("title", out var t) ? t.GetString()
                        : el.TryGetProperty("Title", out var t2) ? t2.GetString() : null;
                    var count = 0;
                    if (el.TryGetProperty("count", out var c) && c.TryGetInt32(out var cn))
                        count = cn;
                    else if (el.TryGetProperty("Count", out var c2) && c2.TryGetInt32(out var cn2))
                        count = cn2;

                    if (count > 0)
                        entries.Add(new PositionPlanEntry(title, count));
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Number
                     && doc.RootElement.TryGetInt32(out var single))
            {
                entries.Add(new PositionPlanEntry(null, single));
            }
        }
        catch (JsonException)
        {
            if (int.TryParse(plannedPositionsJson.Trim(), out var plain))
                entries.Add(new PositionPlanEntry(null, plain));
        }

        return entries;
    }

    public static WorkforceGapResult Analyze(string? plannedPositionsJson, int actualHeadcount)
    {
        var breakdown = ParsePlannedPositions(plannedPositionsJson);
        var plannedTotal = breakdown.Sum(e => e.Count);
        if (plannedTotal <= 0)
            plannedTotal = actualHeadcount;

        var delta = plannedTotal - actualHeadcount;
        return new WorkforceGapResult(
            plannedTotal,
            actualHeadcount,
            Deficit: delta > 0 ? delta : 0,
            Surplus: delta < 0 ? -delta : 0,
            breakdown);
    }
}
