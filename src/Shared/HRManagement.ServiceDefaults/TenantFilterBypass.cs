using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Sanctioned bypass for EF tenant query filters (TEN-002). Requires platform-admin.
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

        return query.IgnoreQueryFilters();
    }
}
