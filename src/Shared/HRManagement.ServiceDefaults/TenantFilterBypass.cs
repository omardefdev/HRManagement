using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

public enum SystemTenantBypassPurpose
{
    SeedInitialization,
    BackgroundQueueProcessing,
    TenantProvisioning,
    OrganizationMaintenance,
    PolicyResolution,
    NationalReferenceLookup,
    PublicCareersPortal
}

/// <summary>
/// Sanctioned bypass for EF tenant query filters (TEN-002). Requires platform-admin with active enter-tenant session.
/// </summary>
public static class TenantFilterBypass
{
    public static IQueryable<T> IgnoreTenantFilter<T>(
        this IQueryable<T> query,
        ITenantContext tenantContext)
        where T : class
    {
        if (!tenantContext.IsPlatformOperator)
        {
            throw new UnauthorizedAccessException(
                "Tenant filter bypass requires the platform-admin role.");
        }

        if (!tenantContext.IsPlatformImpersonating)
        {
            throw new UnauthorizedAccessException(
                "Tenant filter bypass requires an active platform enter-tenant session.");
        }

        return query.IgnoreQueryFilters();
    }

    /// <summary>
    /// Reads a quarantined record for a platform-admin reconciliation operation. Quarantine
    /// resolution is a platform concern, so it may need to locate the orphan before assigning
    /// its tenant; the endpoint remains protected by the platform reconciliation policy.
    /// </summary>
    public static IQueryable<T> IgnoreTenantFilterForPlatformReconciliation<T>(
        this IQueryable<T> query,
        ITenantContext tenantContext)
        where T : class
    {
        if (!tenantContext.IsPlatformOperator)
        {
            throw new UnauthorizedAccessException(
                "Cross-tenant quarantine reconciliation requires the platform-admin role.");
        }

        return query.IgnoreQueryFilters();
    }

    /// <summary>
    /// Cross-tenant system work that has no interactive principal, such as deterministic seeding
    /// and durable background queues. Keep the purpose explicit at every call site so a request
    /// handler cannot acquire a silent, generic bypass.
    /// </summary>
    public static IQueryable<T> IgnoreTenantFilterForSystem<T>(
        this IQueryable<T> query,
        SystemTenantBypassPurpose purpose)
        where T : class
    {
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));

        return query.IgnoreQueryFilters();
    }
}
