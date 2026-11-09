using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-48 (§1, rule 1): a national row (an <see cref="INationalShared"/> entity carrying <see cref="NationalTenant.Id"/>) is
/// written only from the national tenant's session or from an explicit seed scope (<see cref="TenantStampScope"/>). Any added,
/// modified or deleted national row from another session is refused before the save, with
/// <see cref="NationalSharedWriteRefusedException"/> (an <see cref="UnauthorizedAccessException"/>).
/// <para>
/// Under forced row security the database refuses the same writes (<c>tenant_isolation</c> keeps the only WITH CHECK, and
/// <c>national_shared_read</c> is SELECT only). The guard gives a lab that connects as the superuser, where row security is not
/// enforced, the production behaviour.
/// </para>
/// A context built with no tenant context (design-time tooling, <see cref="TenantAwareDbContext.BypassTenantFilter"/>) is not
/// checked, as it is not filtered.
/// </summary>
public sealed class NationalSharedWriteGuard(ITenantContext tenantContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context, tenantContext);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Check(eventData.Context, tenantContext);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>Throws when <paramref name="context"/> holds a national row written outside the national tenant.</summary>
    public static void Check(DbContext? context, ITenantContext tenantContext)
    {
        if (context is null)
            return;
        if (context is TenantAwareDbContext { BypassTenantFilter: true })
            return;
        if (NationalTenant.IsNational(tenantContext.TenantId) || TenantStampScope.IsExplicitAllowed)
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not (BaseEntity entity and INationalShared))
                continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var original = entry.State == EntityState.Added
                ? (Guid?)null
                : entry.Property(nameof(BaseEntity.TenantId)).OriginalValue as Guid?;
            if (entity.TenantId == NationalTenant.Id || original == NationalTenant.Id)
                throw new NationalSharedWriteRefusedException(entity.GetType().Name, entry.State, tenantContext.TenantId);
        }
    }
}

/// <summary>PLAN-48: a national row written outside the national tenant. Translated to 403 with <see cref="MessageAr"/>.</summary>
public sealed class NationalSharedWriteRefusedException(string entityName, EntityState state, Guid? sessionTenantId)
    : UnauthorizedAccessException(
        $"NationalSharedWriteGuard: refusing {state} of the national {entityName} row from tenant {sessionTenantId?.ToString() ?? "(none)"}; " +
        "national settings are written only from the national tenant.")
{
    public const string MessageAr = "الإعدادات الوطنية للقراءة فقط خارج المستأجر الوطني (NationalSharedWriteGuard)";

    public string EntityName { get; } = entityName;

    public EntityState State { get; } = state;

    public Guid? SessionTenantId { get; } = sessionTenantId;
}
