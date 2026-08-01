using HRManagement.SharedKernel.Signatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRManagement.ServiceDefaults.Signatures;

/// <summary>Rejects UPDATE/DELETE on SignatureRecords (SIG-001 append-only).</summary>
public sealed class SignatureImmutabilityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        RejectMutations(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RejectMutations(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void RejectMutations(DbContext? context)
    {
        if (context is null) return;
        foreach (var entry in context.ChangeTracker.Entries<SignatureRecord>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "SignatureRecords are append-only; UPDATE/DELETE are not allowed (SIG-001).");
            }
        }
    }
}
