using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Admin.Logistics.Infrastructure.Services;

/// <summary>
/// PLAN-45: how many requests wait for their approval to start, and how many of them ran out of attempts. Each cycle of
/// <see cref="AssetApprovalStartRetryJob"/> replaces one tenant's picture, so both go back to 0 once nothing waits.
/// </summary>
public static class AssetApprovalMetrics
{
    public const string MeterName = "HRManagement.AssetApproval";

    private static readonly Meter Meter = new(MeterName);
    private static readonly ConcurrentDictionary<Guid, (int Starting, int NeedsIntervention)> ByTenant = new();

    private static readonly ObservableGauge<int> StartingGauge = Meter.CreateObservableGauge(
        "asset_approval_starting_total", () => StartingCount);

    private static readonly ObservableGauge<int> InterventionGauge = Meter.CreateObservableGauge(
        "asset_approval_needs_intervention_total", () => NeedsInterventionCount);

    public static int StartingCount => ByTenant.Values.Sum(tenant => tenant.Starting);

    public static int NeedsInterventionCount => ByTenant.Values.Sum(tenant => tenant.NeedsIntervention);

    public static void Set(Guid tenantId, int starting, int needsIntervention) =>
        ByTenant[tenantId] = (starting, needsIntervention);
}
