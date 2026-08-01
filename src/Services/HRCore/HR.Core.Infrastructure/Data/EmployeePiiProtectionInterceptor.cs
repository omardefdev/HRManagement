using HR.Core.Domain.Entities;
using HRManagement.ServiceDefaults.Privacy;
using HRManagement.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Core.Infrastructure.Data;

/// <summary>Encrypts Employee.NationalId at rest and maintains NationalIdHash for uniqueness.</summary>
public sealed class EmployeePiiProtectionInterceptor(IPiiFieldEncryptionService pii) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Process(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Process(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Process(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<Employee>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            var nationalId = entry.Property(e => e.NationalId).CurrentValue;
            if (string.IsNullOrWhiteSpace(nationalId))
                continue;

            var plaintext = pii.IsProtected(nationalId) ? pii.Unprotect(nationalId) : nationalId.Trim();
            entry.Entity.NationalIdHash = PiiMask.Sha256Hex(plaintext);
            entry.Entity.NationalId = pii.Protect(plaintext);
        }
    }
}

public sealed class EmployeePiiMaterializationInterceptor(IPiiFieldEncryptionService pii) : IMaterializationInterceptor
{
    public object InitializedInstance(MaterializationInterceptionData materializationData, object instance)
    {
        if (instance is Employee emp && !string.IsNullOrEmpty(emp.NationalId))
            emp.NationalId = pii.Unprotect(emp.NationalId);
        return instance;
    }
}
