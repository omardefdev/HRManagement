using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>
/// PLAN-44 (G-WF-12): the one way to insert an instance. A read before an insert leaves a race open, so the two unique indexes decide,
/// and the loser learns which one refused it.
/// </summary>
public static class WorkflowInstanceInsert
{
    public const string ActiveEntityIndex = "IX_WorkflowInstances_Tenant_Entity_Active";
    public const string IdempotencyKeyIndex = "IX_WorkflowInstances_Tenant_Source_IdempotencyKey";

    private const string Savepoint = "wf044_instance_insert";

    /// <returns>Null when the instance was saved, otherwise the name of the index that refused it.</returns>
    public static async Task<string?> TrySaveAsync(HRCoreDbContext db, WorkflowInstance instance, CancellationToken ct)
    {
        // A violation aborts the whole PostgreSQL transaction, and a submission's transaction must live on to link the winner's instance.
        var transaction = db.Database.CurrentTransaction;
        if (transaction is not null)
            await transaction.CreateSavepointAsync(Savepoint, ct);

        db.WorkflowInstances.Add(instance);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (RefusedBy(ex) is not null)
        {
            if (transaction is not null)
                await transaction.RollbackToSavepointAsync(Savepoint, ct);

            db.Entry(instance).State = EntityState.Detached;
            return RefusedBy(ex);
        }

        if (transaction is not null)
            await transaction.ReleaseSavepointAsync(Savepoint, ct);

        return null;
    }

    private static string? RefusedBy(Exception exception) =>
        ConcurrencyExtensions.IsUniqueViolation(exception)
        && ConcurrencyExtensions.TryGetUniqueConstraintName(exception, out var name)
        && name is ActiveEntityIndex or IdempotencyKeyIndex
            ? name
            : null;
}
