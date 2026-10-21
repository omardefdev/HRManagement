using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;

namespace HRManagement.ServiceDefaults.Capacity;

/// <summary>CAP-002: per-tenant quota limits for admission control.</summary>
public sealed class TenantQuota
{
    public int MaxConcurrentRequests { get; init; } = 100;
    public int MaxRequestsPerMinute { get; init; } = 600;
}

/// <summary>CAP-002: admission decision.</summary>
public enum AdmissionDecision
{
    [Display(Name = "قبول")]
    Admit,
    [Display(Name = "رفض")]
    Reject
}

/// <summary>
/// CAP-002: simple in-memory tenant admission control.
/// Prevents a noisy tenant from starving others by enforcing concurrency
/// and rate limits per tenant. Critical journeys (payroll, identity) get
/// priority via separate quota tiers configured in tenant-quota-policy.json.
/// </summary>
public sealed class TenantAdmissionControl
{
    private readonly ConcurrentDictionary<Guid, TenantQuota> _quotas = new();
    private readonly ConcurrentDictionary<Guid, int> _concurrency = new();
    private readonly ConcurrentDictionary<Guid, SlidingWindow> _rateWindows = new();
    private readonly TenantQuota _defaultQuota;

    public TenantAdmissionControl(TenantQuota? defaultQuota = null)
    {
        _defaultQuota = defaultQuota ?? new TenantQuota();
    }

    public void SetQuota(Guid tenantId, TenantQuota quota) =>
        _quotas[tenantId] = quota;

    public TenantQuota GetQuota(Guid tenantId) =>
        _quotas.GetValueOrDefault(tenantId, _defaultQuota);

    public AdmissionDecision TryAdmit(Guid tenantId)
    {
        var quota = GetQuota(tenantId);

        var current = _concurrency.GetValueOrDefault(tenantId, 0);
        if (current >= quota.MaxConcurrentRequests)
            return AdmissionDecision.Reject;

        var window = _rateWindows.GetOrAdd(tenantId, _ => new SlidingWindow());
        if (!window.TryRecord(quota.MaxRequestsPerMinute))
            return AdmissionDecision.Reject;

        _concurrency.AddOrUpdate(tenantId, 1, (_, v) => v + 1);
        return AdmissionDecision.Admit;
    }

    public void Release(Guid tenantId) =>
        _concurrency.AddOrUpdate(tenantId, 0, (_, v) => Math.Max(0, v - 1));

    public int GetCurrentConcurrency(Guid tenantId) =>
        _concurrency.GetValueOrDefault(tenantId, 0);

    /// <summary>One-minute sliding window rate counter.</summary>
    private sealed class SlidingWindow
    {
        private readonly object _lock = new();
        private readonly Queue<DateTime> _timestamps = new();

        public bool TryRecord(int maxPerMinute)
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-1);
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                    _timestamps.Dequeue();

                if (_timestamps.Count >= maxPerMinute)
                    return false;

                _timestamps.Enqueue(DateTime.UtcNow);
                return true;
            }
        }
    }
}
