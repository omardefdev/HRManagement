using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-48 (§1): the one read path of the national shared tables (<see cref="INationalShared"/>). Each method lifts the EF tenant
/// filter and writes the tenant predicate itself, so a national read never rides a general bypass. Under forced row security the
/// <c>national_shared_read</c> policy (SELECT only) gives a tenant session the same national rows; writes stay under
/// <c>tenant_isolation</c> and <see cref="NationalSharedWriteGuard"/>.
/// <para>
/// In the tables with a scope (policy and code policy templates), «national» in the application layer also means
/// <c>Scope = National</c> (D-TEN-1): callers add that predicate, so an agency never reads the trial agency's overrides that
/// live in the national tenant.
/// </para>
/// </summary>
public static class NationalScopeQueryExtensions
{
    /// <summary>
    /// The rows of <paramref name="tenantId"/> and the national rows. With no tenant (null) or the national tenant itself this is
    /// the national rows only. Order with <see cref="OwnFirst{T}"/> when one row must win («my agency first»).
    /// </summary>
    public static IQueryable<T> OwnOrNational<T>(this IQueryable<T> source, Guid? tenantId)
        where T : BaseEntity, INationalShared
    {
        var national = NationalTenant.Id;
        var unfiltered = source.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.NationalSharedRead);
        if (tenantId is not Guid own || own == national)
            return unfiltered.Where(e => e.TenantId == national);

        return unfiltered.Where(e => e.TenantId == own || e.TenantId == national);
    }

    /// <summary>
    /// The session's own rows and the national rows (<see cref="OwnOrNational{T}(IQueryable{T}, Guid?)"/> with the session tenant).
    /// A context built with no tenant context (design-time tooling, <see cref="TenantAwareDbContext.BypassTenantFilter"/>) reads
    /// every row, as its EF tenant filter does.
    /// </summary>
    public static IQueryable<T> OwnOrNational<T>(this IQueryable<T> source, TenantAwareDbContext session)
        where T : BaseEntity, INationalShared =>
        session.BypassTenantFilter
            ? source.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.NationalSharedRead)
            : source.OwnOrNational(session.CurrentTenantId);

    /// <summary>The national rows only, whatever the session tenant.</summary>
    public static IQueryable<T> NationalOnly<T>(this IQueryable<T> source)
        where T : BaseEntity, INationalShared
    {
        var national = NationalTenant.Id;
        return source.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.NationalSharedRead)
            .Where(e => e.TenantId == national);
    }

    /// <summary>«My agency first»: the rows of <paramref name="tenantId"/> before the national ones; continue with ThenBy.</summary>
    public static IOrderedQueryable<T> OwnFirst<T>(this IQueryable<T> source, Guid? tenantId)
        where T : BaseEntity, INationalShared
    {
        var own = tenantId ?? Guid.Empty;
        return source.OrderBy(e => e.TenantId == own ? 0 : 1);
    }

    /// <summary>In memory: the row of <paramref name="tenantId"/> when there is one, otherwise the national row.</summary>
    public static T? PreferOwn<T>(this IEnumerable<T> rows, Guid? tenantId)
        where T : BaseEntity, INationalShared
    {
        T? national = null;
        foreach (var row in rows)
        {
            if (tenantId is Guid own && row.TenantId == own)
                return row;
            if (row.TenantId == NationalTenant.Id)
                national ??= row;
        }

        return national;
    }
}
