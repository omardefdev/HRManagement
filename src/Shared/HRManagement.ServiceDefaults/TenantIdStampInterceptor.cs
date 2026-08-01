using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Opts a server-side operation out of ambient tenant stamping so it can write rows for an
/// explicitly chosen tenant (provisioning, migration, cross-tenant maintenance).
/// <para>
/// Never open this around code that runs on a user request: it disables the check that stops a
/// client from steering a write into another tenant.
/// </para>
/// </summary>
public static class TenantStampScope
{
    private static readonly AsyncLocal<bool> Explicit = new();

    internal static bool IsExplicitAllowed => Explicit.Value;

    public static IDisposable AllowExplicitTenant()
    {
        Explicit.Value = true;
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        public void Dispose() => Explicit.Value = false;
    }
}

/// <summary>
/// Forces <see cref="BaseEntity.TenantId"/> from the ambient tenant on insert, rejects inserts
/// that carry a different tenant, and rejects any attempt to move an existing row to another
/// tenant (TEN-002 / TEN-002C).
/// <para>
/// <see cref="BaseEntity.TenantId"/> is a public settable property, so any controller action that
/// model-binds an entity lets a client post another agency's tenant id. Stamping only when the
/// value was empty left that value intact, and with row level security ineffective for the owning
/// role nothing else stopped the write.
/// </para>
/// </summary>
public sealed class TenantIdStampInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampAndValidate(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampAndValidate(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StampAndValidate(DbContext? context)
    {
        if (context is null)
            return;

        var ambientTenantId = tenantContext.TenantId;
        var mayChooseTenant = tenantContext.IsPlatformOperator || TenantStampScope.IsExplicitAllowed;

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            if (TenantEntityClassification.IsGlobalEntity(entry.Entity.GetType()))
                continue;

            var typeName = entry.Entity.GetType().Name;
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.TenantId = ResolveTenantForInsert(
                        entry.Entity.TenantId, ambientTenantId, mayChooseTenant, typeName);
                    break;
                case EntityState.Modified:
                    RejectTenantReassignment(entry, typeName, mayChooseTenant);
                    break;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<OrgUnitSnapshot>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.TenantId = ResolveTenantForInsert(
                        entry.Entity.TenantId, ambientTenantId, mayChooseTenant, nameof(OrgUnitSnapshot));
                    break;
                case EntityState.Modified:
                    RejectTenantReassignment(entry, nameof(OrgUnitSnapshot), mayChooseTenant);
                    break;
            }
        }
    }

    private static Guid ResolveTenantForInsert(
        Guid suppliedTenantId,
        Guid? ambientTenantId,
        bool mayChooseTenant,
        string typeName)
    {
        if (ambientTenantId.HasValue)
        {
            if (suppliedTenantId == Guid.Empty || suppliedTenantId == ambientTenantId.Value)
                return ambientTenantId.Value;

            if (mayChooseTenant)
                return suppliedTenantId;

            throw new UnauthorizedAccessException(
                $"Refusing to insert {typeName} for tenant {suppliedTenantId} " +
                $"while the request is scoped to tenant {ambientTenantId.Value}.");
        }

        // No ambient tenant: background work, provisioning, or seeding. The value must still be
        // explicit, which keeps the original TEN-002 guarantee that nothing lands untenanted.
        if (suppliedTenantId == Guid.Empty)
            throw new InvalidOperationException($"Cannot insert {typeName} without TenantId.");

        return suppliedTenantId;
    }

    private static void RejectTenantReassignment(EntityEntry entry, string typeName, bool mayChooseTenant)
    {
        var property = entry.Property(nameof(BaseEntity.TenantId));
        if (!property.IsModified)
            return;

        var original = property.OriginalValue as Guid? ?? Guid.Empty;
        var current = property.CurrentValue as Guid? ?? Guid.Empty;
        if (original == current)
            return;

        if (mayChooseTenant)
            return;

        throw new UnauthorizedAccessException(
            $"Refusing to move {typeName} from tenant {original} to tenant {current}.");
    }
}
