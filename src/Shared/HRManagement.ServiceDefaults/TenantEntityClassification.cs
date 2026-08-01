using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.Tenancy;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Classifies entities for tenant filtering (TEN-002).
/// Global/infra types are not filtered and are not rejected by the stamp interceptor.
/// See docs/TENANT_ENTITY_CLASSIFICATION.md for the full inventory.
/// </summary>
public static class TenantEntityClassification
{
    /// <summary>
    /// Platform / cross-tenant types: no EF global filter, no RLS (by design).
    /// EntityAuditLog stays global so platform-admin can audit across tenants;
    /// query paths must still apply an explicit TenantId predicate for tenant users.
    /// </summary>
    private static readonly HashSet<Type> GlobalEntities =
    [
        typeof(EntityAuditLog),
        typeof(Tenant),
        typeof(TenantAssignmentRule),
        typeof(TenantQuarantineEntry),
        typeof(SodRule),
        typeof(ConflictingRolePair)
    ];

    public static IReadOnlyCollection<Type> GlobalEntityTypes => GlobalEntities;

    public static bool IsGlobalEntity(Type type)
    {
        var t = type;
        while (t is not null)
        {
            if (GlobalEntities.Contains(t))
                return true;
            t = t.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Tenant-owned BaseEntity descendants get EF filters + RLS.
    /// Non-BaseEntity tenant rows (IdempotencyRecord, OrgUnitSnapshot) are handled
    /// explicitly in <see cref="TenantQueryFilterExtensions"/>.
    /// </summary>
    public static bool IsTenantOwned(Type type) =>
        typeof(BaseEntity).IsAssignableFrom(type) && !IsGlobalEntity(type);
}
