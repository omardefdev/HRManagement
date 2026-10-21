using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// DbContext base that exposes tenant properties for EF global query filters (TEN-002).
/// Filters reference <see cref="EffectiveTenantId"/> / <see cref="BypassTenantFilter"/> so EF
/// re-evaluates them per instance at query time.
/// </summary>
public abstract class TenantAwareDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    protected TenantAwareDbContext(DbContextOptions options, ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext ?? DesignTimeTenantContext.Instance;
    }

    /// <summary>Current request/job tenant; null when unset.</summary>
    public Guid? CurrentTenantId => _tenantContext.TenantId;

    /// <summary>
    /// Non-null tenant id used in query filters. Guid.Empty when unset (fail-closed: no rows match).
    /// </summary>
    public Guid EffectiveTenantId => _tenantContext.TenantId ?? Guid.Empty;

    /// <summary>When true, global query filters admit all tenants (design-time tooling only).</summary>
    public bool BypassTenantFilter => _tenantContext is DesignTimeTenantContext;

    protected ITenantContext TenantContext => _tenantContext;
}
