namespace HR.Core.Domain.Services;

/// <summary>HCM-031: a passed candidate as allocation sees it (rank order, preferences in order).</summary>
public sealed record AllocationCandidate(Guid ApplicationId, int Rank, string ExamNumber, decimal FinalScore, IReadOnlyList<Guid> PreferredLineIds);

/// <summary>HCM-031: a line as allocation sees it — its requisitions in assignment order and its effective pass mark.</summary>
public sealed record AllocationLine(Guid LineId, IReadOnlyList<Guid> RequisitionIds, decimal PassMark);

public sealed record AllocationPlacement(Guid ApplicationId, Guid LineId, Guid RequisitionId, int PreferenceOrder);

public sealed record AllocationReserve(Guid ApplicationId, Guid LineId, int LineRank);

public sealed record AllocationPlan(
    IReadOnlyList<AllocationPlacement> Placements,
    IReadOnlyList<AllocationReserve> Reserves,
    IReadOnlyList<Guid> NotPlaced);

/// <summary>
/// HCM-031 (PLAN-31, AC-HCM-31f): allocation by merit and preference. Passed candidates are taken in rank order (ties on
/// a rank — only possible after an HR tie decision kept equal ranks — fall back to the exam number); each takes the
/// highest-preference line that still has a vacancy and whose own pass mark the candidate reaches; within a line the
/// vacancies are filled in the given requisition order (requisition number). A passed candidate left without a line is
/// put on the reserve list of every preferred line he passed (line rank by overall rank); otherwise not placed.
/// </summary>
public static class CompetitionAllocationRules
{
    public static AllocationPlan Plan(IReadOnlyList<AllocationCandidate> passed, IReadOnlyList<AllocationLine> lines)
    {
        var byLine = lines.ToDictionary(l => l.LineId);
        var used = lines.ToDictionary(l => l.LineId, _ => 0);
        var placements = new List<AllocationPlacement>();
        var reserves = new Dictionary<Guid, List<AllocationCandidate>>();
        var notPlaced = new List<Guid>();

        foreach (var candidate in passed.OrderBy(c => c.Rank).ThenBy(c => c.ExamNumber, StringComparer.Ordinal))
        {
            AllocationPlacement? placement = null;
            for (var order = 0; order < candidate.PreferredLineIds.Count && placement is null; order++)
            {
                var lineId = candidate.PreferredLineIds[order];
                if (!byLine.TryGetValue(lineId, out var line) || candidate.FinalScore < line.PassMark)
                    continue;
                if (used[lineId] >= line.RequisitionIds.Count)
                    continue;
                placement = new AllocationPlacement(candidate.ApplicationId, lineId, line.RequisitionIds[used[lineId]], order + 1);
                used[lineId]++;
            }

            if (placement is not null)
            {
                placements.Add(placement);
                continue;
            }

            var reserveLines = candidate.PreferredLineIds
                .Where(id => byLine.TryGetValue(id, out var line) && candidate.FinalScore >= line.PassMark)
                .Distinct()
                .ToList();
            if (reserveLines.Count == 0)
            {
                notPlaced.Add(candidate.ApplicationId);
                continue;
            }
            foreach (var lineId in reserveLines)
            {
                if (!reserves.TryGetValue(lineId, out var list))
                    reserves[lineId] = list = [];
                list.Add(candidate);
            }
        }

        var reserveRows = reserves
            .SelectMany(kv => kv.Value.Select((c, i) => new AllocationReserve(c.ApplicationId, kv.Key, i + 1)))
            .ToList();
        return new AllocationPlan(placements, reserveRows, notPlaced);
    }
}
