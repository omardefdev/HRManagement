using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Contracts.Infrastructure.Services;

/// <summary>What one tenant's pass changed.</summary>
public sealed record SeparationReconcileResult(int Records, int Items, int ItemsAdded, int Links)
{
    public int Total => Records + Items + ItemsAdded + Links;
}

/// <summary>
/// PLAN-60 (P-5, P-18): after a rollback, PLAN-59's tag runs on the migrated schema and moves only the old flags; when this tag starts
/// again, this puts back in line what it changed, by the backfill's rules 1, 2, 6 and 7, inside each tenant, idempotently — as
/// <c>WorkflowPackSeeder.BackfillGovernanceAsync</c> does in HR.Core. A record whose flags agree with its status is never touched.
/// </summary>
public static class SeparationStatusReconciler
{
    /// <summary>Every tenant that ever saved a separation (the audit log is outside row security), and the default one.</summary>
    public static async Task RunAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        List<Guid> tenants;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            tenants = await db.AuditLogs.AsNoTracking()
                .Where(l => (l.EntityType == nameof(SeparationRecord) || l.EntityType == nameof(ServiceTransaction)) && l.TenantId != Guid.Empty)
                .Select(l => l.TenantId)
                .Distinct()
                .ToListAsync(ct);
        }

        if (!tenants.Contains(TenantDefaults.DefaultTenantId))
            tenants.Add(TenantDefaults.DefaultTenantId);

        foreach (var tenant in tenants)
        {
            var previous = TenantIdFlow.Get();
            TenantIdFlow.Set(tenant);
            try
            {
                using var scope = services.CreateScope();
                var result = await ReconcileAsync(scope.ServiceProvider.GetRequiredService<ContractsDbContext>(), ct);
                if (result.Total > 0)
                    logger.LogWarning(
                        "SEP060 reconciler, tenant {TenantId}: {Records} record(s), {Items} item(s), {Added} item(s) added, {Links} link(s).",
                        tenant, result.Records, result.Items, result.ItemsAdded, result.Links);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A repair: the next start tries again. One tenant's failure does not hide the others.
                logger.LogError(ex, "SEP060 reconciler failed for tenant {TenantId}.", tenant);
            }
            finally
            {
                TenantIdFlow.Set(previous);
            }
        }
    }

    /// <summary>One tenant (the caller sets it in <see cref="TenantIdFlow"/>). Saves the statuses, then the items and the links.</summary>
    public static async Task<SeparationReconcileResult> ReconcileAsync(ContractsDbContext db, CancellationToken ct = default)
    {
        // Rules 1 and 2 (and 4): a record cancelled while open, published while not yet executed, or a draft with no employee.
        var executedOrLater = new[] { SeparationStatus.Executing, SeparationStatus.AwaitingIntervention, SeparationStatus.Completed };
        var disagreeing = await db.SeparationRecords
            .Where(r => (r.IsCancelled && r.Status != SeparationStatus.Cancelled)
                        || (r.SeparationEventPublished && !executedOrLater.Contains(r.Status) && r.Status != SeparationStatus.Cancelled)
                        || (r.EmployeeId == Guid.Empty && r.Status == SeparationStatus.Draft))
            .ToListAsync(ct);
        var records = 0;
        foreach (var record in disagreeing)
        {
            var saga = await db.SeparationSagaStates.AsNoTracking()
                .Where(s => s.SeparationRecordId == record.Id)
                .OrderByDescending(s => s.UpdatedAt)
                .Select(s => s.CurrentState)
                .FirstOrDefaultAsync(ct);
            if (SeparationWorkflowRules.ReconcileFromLegacyFlags(record, saga))
                records++;
        }

        // Saved first: the next rules read which records are open from the database.
        if (records > 0)
            await db.SaveChangesAsync(ct);

        // Rule 6: an item the previous tag inserted carries the column's default (Manual, no party); every mode follows its step.
        var items = 0;
        foreach (var item in await db.SeparationChecklists
                     .Where(c => c.CompletionMode == SeparationStepCompletionMode.Manual && c.Step != SeparationStep.EmployeeNotified)
                     .ToListAsync(ct))
        {
            item.CompletionMode = SeparationChecklistDefaults.ModeFor(item.Step);
            item.PartyName = SeparationChecklistDefaults.PartyFor(item.Step);
            items++;
        }

        // Rule 6: the items an open record lacks (a record the previous tag created has the six older ones).
        var open = SeparationWorkflowRules.OpenStatuses.ToList();
        var openRecords = await db.SeparationRecords.AsNoTracking()
            .Where(r => open.Contains(r.Status))
            .Select(r => new { r.Id, r.TenantId })
            .ToListAsync(ct);
        var openIds = openRecords.Select(r => r.Id).ToList();
        var present = (await db.SeparationChecklists.AsNoTracking()
                .Where(c => openIds.Contains(c.SeparationRecordId))
                .Select(c => new { c.SeparationRecordId, c.Step })
                .ToListAsync(ct))
            .ToLookup(c => c.SeparationRecordId, c => c.Step);
        var added = 0;
        foreach (var openRecord in openRecords)
        {
            foreach (var step in SeparationChecklistDefaults.DefaultSteps.Except(present[openRecord.Id]))
            {
                // The record's own tenant, explicitly: an item the tenant filter cannot see would be added again on the next start.
                var item = SeparationChecklistDefaults.Create(openRecord.Id, step);
                item.TenantId = openRecord.TenantId;
                db.SeparationChecklists.Add(item);
                added++;
            }
        }

        // Rule 7: a Separation transaction with no record, to the nearest record of its employee created within 5 minutes that has none.
        var links = 0;
        var unlinked = await db.ServiceTransactions
            .Where(t => t.TransactionType == ServiceTransactionType.Separation && t.SeparationRecordId == null)
            .ToListAsync(ct);
        if (unlinked.Count > 0)
        {
            var linked = (await db.ServiceTransactions.AsNoTracking()
                    .Where(t => t.SeparationRecordId != null)
                    .Select(t => t.SeparationRecordId!.Value)
                    .ToListAsync(ct))
                .ToHashSet();
            var employees = unlinked.Select(t => t.EmployeeId).Distinct().ToList();
            var candidates = await db.SeparationRecords.AsNoTracking()
                .Where(r => employees.Contains(r.EmployeeId))
                .Select(r => new { r.Id, r.EmployeeId, r.CreatedAt })
                .ToListAsync(ct);
            var pairs = (from t in unlinked
                         from r in candidates
                         where r.EmployeeId == t.EmployeeId && !linked.Contains(r.Id)
                         let distance = Math.Abs((t.CreatedAt - r.CreatedAt).TotalSeconds)
                         where distance <= 300
                         orderby distance, r.Id, t.Id
                         select (Transaction: t, RecordId: r.Id)).ToList();
            var usedTransactions = new HashSet<Guid>();
            foreach (var (transaction, recordId) in pairs)
            {
                if (linked.Contains(recordId) || !usedTransactions.Add(transaction.Id))
                    continue;
                transaction.SeparationRecordId = recordId;
                linked.Add(recordId);
                links++;
            }
        }

        if (items + added + links > 0)
            await db.SaveChangesAsync(ct);
        return new SeparationReconcileResult(records, items, added, links);
    }
}
