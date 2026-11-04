using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.Workflow;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>
/// PLAN-40 (§6, D-40-4): replays the custody outcomes lost before the fix (Admin.Logistics ignores anything no longer
/// pending approval) and re-synchronises the org units (the consumers upsert by id). <c>IPolicyPublished</c> is not
/// replayed (the policy cache is built at start-up) and <c>IAgencyOnboarded</c> has no consumer. Reads and publishes inside
/// the caller's tenant.
/// </summary>
public sealed class HrCoreMessagingRepair(HRCoreDbContext db, IPublishEndpoint publish, OrgUnitIntegrationService orgUnits)
{
    public const string AssetOutcomesKind = "asset-outcomes";
    public const string OrgUnitsKind = "org-units";

    public async Task<MessagingRepairResult> ReplayAssetOutcomesAsync(bool dryRun, Guid? actor, CancellationToken ct = default)
    {
        var finished = await db.WorkflowInstances
            .Where(i => (i.EntityType == WorkflowAssetOutcomes.AssignmentEntityType || i.EntityType == WorkflowAssetOutcomes.LossEntityType)
                        && (i.Status == WorkflowStatus.Completed || i.Status == WorkflowStatus.Rejected))
            .OrderBy(i => i.Id)
            .ToListAsync(ct);

        var ledgerTypes = WorkflowAssetOutcomes.LedgerEntityTypes;
        var ids = finished.Select(i => i.Id).ToList();
        var published = await db.AuditLogs
            .Where(a => ledgerTypes.Contains(a.EntityType) && ids.Contains(a.EntityId))
            .Select(a => a.EntityId)
            .ToListAsync(ct);
        var lost = finished.Where(i => !published.Contains(i.Id)).ToList();

        var lostIds = lost.Select(i => i.Id).ToList();
        if (dryRun || lost.Count == 0)
            return new MessagingRepairResult(AssetOutcomesKind, dryRun, lost.Count, lostIds);

        await OutboxUnitOfWork.ExecuteAsync(db, async token =>
        {
            foreach (var instance in lost)
            {
                var approved = instance.Status == WorkflowStatus.Completed;
                await WorkflowAssetOutcomes.PublishOutcomeAsync(db, publish, instance, approved, RejectionReason(instance), actor, PublishedEventLedger.Replayed, token);
            }

            db.AuditLogs.Add(PublishedEventLedger.ReplayRun(AssetOutcomesKind, lost.Count, actor));
        }, ct);
        return new MessagingRepairResult(AssetOutcomesKind, false, lost.Count, lostIds);
    }

    public async Task<MessagingRepairResult> ResyncOrgUnitsAsync(bool dryRun, Guid? actor, CancellationToken ct = default)
    {
        var ids = await db.OrgUnits.AsNoTracking().OrderBy(u => u.Id).Select(u => u.Id).ToListAsync(ct);
        if (dryRun || ids.Count == 0)
            return new MessagingRepairResult(OrgUnitsKind, dryRun, ids.Count, ids);

        await OutboxUnitOfWork.ExecuteAsync(db, async token =>
        {
            db.AuditLogs.Add(PublishedEventLedger.ReplayRun(OrgUnitsKind, ids.Count, actor));
            await orgUnits.PublishAllOrgUnitsAsync(token);
        }, ct);
        return new MessagingRepairResult(OrgUnitsKind, false, ids.Count, ids);
    }

    /// <summary>The comment of the rejecting step, the reason the original event would have carried.</summary>
    private static string? RejectionReason(WorkflowInstance instance)
    {
        if (instance.Status != WorkflowStatus.Rejected)
            return null;
        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson);
        return history?.LastOrDefault()?.Comments;
    }
}
