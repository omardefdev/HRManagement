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
    private static readonly ConcurrentDictionary<Guid, IReadOnlyDictionary<string, int>> UnappliedByTenant = new();
    private static readonly ConcurrentDictionary<string, byte> TypesSeen = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Counter<long> Conflicts = Meter.CreateCounter<long>("workflow_instance_conflict_total");

    private static readonly ObservableGauge<int> UnappliedGauge = Meter.CreateObservableGauge(
        "workflow_outcome_unapplied_total",
        () => TypesSeen.Keys.Select(type =>
            new Measurement<int>(UnappliedCount(type), new KeyValuePair<string, object?>("entity_type", type))));

    public static void RecordConflict(string action) =>
        Conflicts.Add(1, new KeyValuePair<string, object?>("action", action));

    /// <summary>Replaces one tenant's picture. A type seen before and clean now reports 0, not nothing.</summary>
    public static void SetUnapplied(Guid tenantId, IReadOnlyDictionary<string, int> byEntityType)
    {
        UnappliedByTenant[tenantId] = byEntityType;
        foreach (var type in byEntityType.Keys)
            TypesSeen.TryAdd(type, 0);
    }

    public static int UnappliedCount(string entityType) =>
        UnappliedByTenant.Values.Sum(tenant => tenant.GetValueOrDefault(entityType));
}
