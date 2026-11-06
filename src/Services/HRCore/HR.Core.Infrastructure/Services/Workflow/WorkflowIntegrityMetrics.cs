using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>
/// PLAN-44: <c>workflow_instance_conflict_total{action}</c> counts decisions that lost a race. <c>workflow_outcome_unapplied_total{entity_type}</c>
/// is the number of finished instances whose outcome the last reconciliation found unapplied: it goes back to 0 once they are repaired.
/// </summary>
public static class WorkflowIntegrityMetrics
{
    public const string MeterName = "HRManagement.WorkflowIntegrity";

    private static readonly Meter Meter = new(MeterName);
    private static readonly ConcurrentDictionary<string, int> Unapplied = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Counter<long> Conflicts = Meter.CreateCounter<long>("workflow_instance_conflict_total");

    private static readonly ObservableGauge<int> UnappliedGauge = Meter.CreateObservableGauge(
        "workflow_outcome_unapplied_total",
        () => Unapplied.Select(kv => new Measurement<int>(kv.Value, new KeyValuePair<string, object?>("entity_type", kv.Key))));

    public static void RecordConflict(string action) =>
        Conflicts.Add(1, new KeyValuePair<string, object?>("action", action));

    /// <summary>Replaces the picture of the last run. A type seen before and clean now reports 0, not nothing.</summary>
    public static void SetUnapplied(IReadOnlyDictionary<string, int> byEntityType)
    {
        foreach (var key in Unapplied.Keys.Except(byEntityType.Keys, StringComparer.OrdinalIgnoreCase))
            Unapplied[key] = 0;
        foreach (var (entityType, count) in byEntityType)
            Unapplied[entityType] = count;
    }

    public static int UnappliedCount(string entityType) => Unapplied.GetValueOrDefault(entityType);
}
