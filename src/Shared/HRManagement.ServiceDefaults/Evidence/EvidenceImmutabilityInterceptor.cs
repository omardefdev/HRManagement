using HRManagement.SharedKernel.Evidence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRManagement.ServiceDefaults.Evidence;

/// <summary>
/// Rejects UPDATE/DELETE on EvidenceRecords (EVD-001) and EvidenceArtifacts / EvidencePackExports (EVD-003).
/// </summary>
public sealed class EvidenceImmutabilityInterceptor : SaveChangesInterceptor
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

        foreach (var entry in context.ChangeTracker.Entries<EvidenceRecord>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "EvidenceRecords are append-only; UPDATE/DELETE are not allowed (EVD-001).");
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<EvidenceArtifact>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "EvidenceArtifacts are append-only; UPDATE/DELETE are not allowed (EVD-003).");
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<EvidencePackExport>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "EvidencePackExports are append-only; UPDATE/DELETE are not allowed (EVD-003).");
            }
        }
    }
}
