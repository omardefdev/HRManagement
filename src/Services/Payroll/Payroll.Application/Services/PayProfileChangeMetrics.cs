using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Payroll.Application.Services;

/// <summary>
/// PLAN-53 (P-7): the scheduled salary changes whose activation was refused (a frozen month, the grade's bounds) and that wait for an
/// approver, per agency. The gauge reports their total; the activation job and the manual endpoint set an agency's count after a cycle.
/// </summary>
public static class PayProfileChangeMetrics
{
    public const string MeterName = "HRManagement.PayrollSalaryChange";
    public const string BlockedGaugeName = "payroll_salary_change_apply_blocked";

    private static readonly Meter Meter = new(MeterName);
    private static readonly ConcurrentDictionary<Guid, int> BlockedByTenant = new();

    private static readonly ObservableGauge<int> BlockedGauge = Meter.CreateObservableGauge(BlockedGaugeName, () => BlockedCount);

    public static int BlockedCount => BlockedByTenant.Values.Sum();

    public static int BlockedOf(Guid tenantId) => BlockedByTenant.TryGetValue(tenantId, out var count) ? count : 0;

    public static void SetBlocked(Guid tenantId, int blocked) => BlockedByTenant[tenantId] = Math.Max(0, blocked);

    /// <summary>Touches the gauge so the meter exists before the first cycle (and keeps the field from being trimmed as unused).</summary>
    public static bool IsRegistered => BlockedGauge is not null;
}
