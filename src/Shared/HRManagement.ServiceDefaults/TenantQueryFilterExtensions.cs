using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Applies EF global query filters for tenant-owned entities (TEN-002).
/// </summary>
public static class TenantQueryFilterExtensions
{
    public static void ConfigureTenantQueryFilters(this ModelBuilder modelBuilder, TenantAwareDbContext context)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            if (clr is null || clr.IsAbstract)
                continue;

            if (typeof(BaseEntity).IsAssignableFrom(clr)
                && TenantEntityClassification.IsTenantOwned(clr))
            {
                SetBaseEntityFilter(modelBuilder, context, clr);
            }
        }

        if (modelBuilder.Model.FindEntityType(typeof(OrgUnitSnapshot)) is not null)
        {
            modelBuilder.Entity<OrgUnitSnapshot>(entity =>
            {
                entity.HasQueryFilter(s =>
                    context.BypassTenantFilter || s.TenantId == context.EffectiveTenantId);
                entity.HasIndex(s => s.ParentId);
                entity.HasIndex(s => new { s.TenantId, s.MaterializedPath });
            });
        }

        if (modelBuilder.Model.FindEntityType(typeof(EmployeeOrgUnitSnapshot)) is not null)
        {
            modelBuilder.Entity<EmployeeOrgUnitSnapshot>(entity =>
            {
                entity.HasKey(e => e.EmployeeId);
                entity.HasIndex(e => e.PrimaryOrgUnitId);
                entity.HasQueryFilter(e =>
                    context.BypassTenantFilter || e.TenantId == context.EffectiveTenantId);
            });
        }

        // IdempotencyRecord is not BaseEntity — apply an explicit tenant filter (TEN-002).
        if (modelBuilder.Model.FindEntityType(typeof(IdempotencyRecord)) is not null)
        {
            modelBuilder.Entity<IdempotencyRecord>().HasQueryFilter(e =>
                context.BypassTenantFilter || e.TenantId == context.EffectiveTenantId);
        }
    }

    private static void SetBaseEntityFilter(ModelBuilder modelBuilder, TenantAwareDbContext context, Type clr)
    {
        var method = typeof(TenantQueryFilterExtensions)
            .GetMethod(nameof(SetTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(clr);
        method.Invoke(null, [modelBuilder, context]);
    }

    private static void SetTenantFilter<TEntity>(ModelBuilder modelBuilder, TenantAwareDbContext context)
        where TEntity : BaseEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            context.BypassTenantFilter || e.TenantId == context.EffectiveTenantId);
    }
}
