using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRManagement.ServiceDefaults;

public sealed class AuditSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditFields(DbContext? context)
    {
        if (context is null) return;

        RejectEntityAuditMutations(context);
        UtcDateTimeNormalization.Apply(context);

        var userId = GetCurrentUserId();
        var auditEntries = new List<EntityAuditLog>();
        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Entity audit rows themselves must not generate recursive audit.
            if (entry.Entity is EntityAuditLog)
                continue;

            if (entry.Entity is BaseEntity baseEntity)
                baseEntity.UpdatedAt = now;

            if (entry.Entity is AuditableEntity auditable)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditable.CreatedBy ??= userId;
                        auditable.UpdatedBy = userId;
                        break;
                    case EntityState.Modified:
                        auditable.UpdatedBy = userId;
                        break;
                }
            }

            if (entry.Entity is BaseEntity be && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                var auditLog = new EntityAuditLog
                {
                    EntityType = entry.Entity.GetType().Name,
                    EntityId = be.Id,
                    Action = entry.State switch
                    {
                        EntityState.Added => "Created",
                        EntityState.Modified => "Updated",
                        EntityState.Deleted => "Deleted",
                        _ => entry.State.ToString()
                    },
                    UserId = userId
                };

                if (entry.State == EntityState.Modified)
                {
                    var changed = entry.Properties
                        .Where(p => p.IsModified)
                        .ToDictionary(
                            p => p.Metadata.Name,
                            p => new
                            {
                                Old = RedactIfNeeded(entry.Entity, p.Metadata.Name, p.OriginalValue),
                                New = RedactIfNeeded(entry.Entity, p.Metadata.Name, p.CurrentValue)
                            });

                    if (changed.Count > 0)
                        auditLog.ChangedFields = JsonSerializer.Serialize(changed);
                }

                auditEntries.Add(auditLog);
            }
        }

        if (auditEntries.Count > 0)
        {
            var auditSet = context.Set<EntityAuditLog>();
            auditSet.AddRange(auditEntries);
        }

        UtcDateTimeNormalization.Apply(context);
    }

    private static void RejectEntityAuditMutations(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<EntityAuditLog>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "EntityAuditLog rows are append-only; updates and deletes are not allowed.");
            }
        }
    }

    private static string? RedactIfNeeded(object entity, string propertyName, object? value)
    {
        if (value is null)
            return null;

        var prop = entity.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop?.GetCustomAttribute<AuditRedactAttribute>() is not null)
            return "***";

        return value.ToString();
    }

    private Guid? GetCurrentUserId()
    {
        var sub = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? httpContextAccessor.HttpContext?.User?.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
