using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow;

public sealed record ApprovalRepairItem(
    string EntityType,
    Guid EntityId,
    string? Reference,
    string Action,
    string Result,
    string? Error,
    Guid? WorkflowInstanceId);

/// <summary>PLAN-44: what a run looks at. The reconciliation job never starts or cancels an instance, so it runs <see cref="Outcomes"/>.</summary>
public enum ApprovalRepairScope
{
    All,
    Outcomes
}

public sealed record ApprovalRepairReport(bool DryRun, DateTime RanAt, IReadOnlyList<ApprovalRepairItem> Items)
{
    public int Found => Items.Count;
    public int Repaired => Items.Count(i => i.Result == ApprovalRepairService.Repaired);
    public int Failed => Items.Count(i => i.Result == ApprovalRepairService.Failed);
    public IReadOnlyDictionary<string, int> ByEntityType =>
        Items.GroupBy(i => i.EntityType).ToDictionary(g => g.Key, g => g.Count());
}

/// <summary>
/// PLAN-25 (G-02 / G-14): finds, and unless <c>dryRun</c> repairs, approvals left behind by builds before PLAN-25 — in
/// the caller's tenant (the context's tenant filter applies). Repairs only start the missing workflow (through the same
/// coordinator as a submit, audited <see cref="ApprovalRestartRules.RestartAuditAction"/>) or cancel orphan
/// RECRUITMENT-HIRE instances; no other business data is changed. Idempotent: a second run finds nothing.
/// <para>
/// PLAN-44 (G-WF-12), a second category: a finished instance whose linked entity is still pending, because a decision and its effect were
/// separate saves before this plan. The repair runs the handler again and audits the operator; it creates and changes no instance. A
/// completed instance the entity does not link is listed as «غير مرتبطة» and never applied. External types are not here: their effect
/// is an outbox event in the decision's transaction, and their owners repair them (PLAN-45, PLAN-46).
/// </para>
/// </summary>
public sealed class ApprovalRepairService(
    HRCoreDbContext db,
    RecruitmentRequisitionLifecycleService requisitions,
    RecruitmentOfferService offers,
    SelectionCommitteeLifecycleService committees,
    RecruitmentAppealService appeals,
    EstablishmentChangeLifecycleService establishment,
    IWorkflowInstanceActionService outcomes)
{
    public const string Listed = "Listed";
    public const string Repaired = "Repaired";
    public const string Failed = "Failed";
    public const string StartWorkflowAction = "StartWorkflow";
    public const string CancelOrphanAction = "CancelOrphanHireWorkflow";
    public const string ApplyOutcomeAction = "ApplyFinishedOutcome";
    public const string NotLinked = "NotLinked";
    public const string NotLinkedMessage = "غير مرتبطة";
    public const string OutcomeUnchangedMessage = "لم يغيّر المعالج حالة الكيان.";
    public const string OutcomeReappliedAuditAction = "WorkflowOutcomeReapplied";
    public const string OrphanHireEntityType = "RecruitmentApplication";
    public const string OrphanHireCancelAuditAction = "OrphanHireWorkflowCancelled";
    public const string OrphanHireCancelReason =
        "PLAN-25 (G-14): مسار RECRUITMENT-HIRE أُلغي — التعيين محكوم باعتماد خطاب العرض.";

    public async Task<ApprovalRepairReport> RunAsync(
        bool dryRun,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct = default,
        ApprovalRepairScope scope = ApprovalRepairScope.All)
    {
        var items = new List<ApprovalRepairItem>();
        if (scope == ApprovalRepairScope.All)
            await RepairMissingWorkflowsAsync(items, dryRun, actorUserId, actorUserName, ct);

        var unapplied = await RepairFinishedOutcomesAsync(items, dryRun, actorUserId, ct);
        WorkflowIntegrityMetrics.SetUnapplied(db.CurrentTenantId ?? Guid.Empty, unapplied);

        return new ApprovalRepairReport(dryRun, DateTime.UtcNow, items);
    }

    private async Task RepairMissingWorkflowsAsync(
        List<ApprovalRepairItem> items, bool dryRun, Guid? actorUserId, string? actorUserName, CancellationToken ct)
    {
        var stuckRequisitions = await db.RecruitmentRequisitions.AsNoTracking()
            .Where(r => r.Status == RequisitionStatus.PendingApproval && r.WorkflowInstanceId == null)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Id, r.RequisitionNumber })
            .ToListAsync(ct);
        foreach (var r in stuckRequisitions)
            items.Add(await RepairAsync("RecruitmentRequisition", r.Id, r.RequisitionNumber, dryRun,
                () => requisitions.RestartApprovalAsync(r.Id, actorUserId, actorUserName, ct),
                () => InstanceOfAsync(db.RecruitmentRequisitions.Where(x => x.Id == r.Id).Select(x => x.WorkflowInstanceId), ct)));

        var stuckOffers = await db.OfferLetters.AsNoTracking()
            .Where(o => o.Status == OfferStatus.PendingApproval && o.WorkflowInstanceId == null)
            .OrderBy(o => o.CreatedAt)
            .Select(o => new { o.Id, o.OfferNumber })
            .ToListAsync(ct);
        foreach (var o in stuckOffers)
            items.Add(await RepairAsync("OfferLetter", o.Id, o.OfferNumber, dryRun,
                () => offers.RestartApprovalAsync(o.Id, actorUserId, actorUserName, ct),
                () => InstanceOfAsync(db.OfferLetters.Where(x => x.Id == o.Id).Select(x => x.WorkflowInstanceId), ct)));

        var stuckSessions = await db.SelectionCommitteeSessions.AsNoTracking()
            .Where(s => s.SubmittedAt != null && s.ApprovedAt == null && s.WorkflowInstanceId == null)
            .OrderBy(s => s.SubmittedAt)
            .Select(s => new { s.Id })
            .ToListAsync(ct);
        foreach (var s in stuckSessions)
            items.Add(await RepairAsync("SelectionCommitteeSession", s.Id, null, dryRun,
                () => committees.RestartApprovalAsync(s.Id, actorUserId, actorUserName, ct),
                () => InstanceOfAsync(db.SelectionCommitteeSessions.Where(x => x.Id == s.Id).Select(x => x.WorkflowInstanceId), ct)));

        // HCM-028 rules: only appeals with an assigned reviewer that are not waiting for information get a review workflow.
        var stuckAppeals = (await db.RecruitmentAppeals.AsNoTracking()
                .Where(a => a.Status == RecruitmentAppealStatus.Pending && a.WorkflowInstanceId == null
                    && (a.IndependentReviewerEmployeeId != null || a.IndependentReviewerId != null))
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct))
            .Where(a => !a.IsAwaitingInfo)
            .ToList();
        foreach (var a in stuckAppeals)
            items.Add(await RepairAsync(nameof(RecruitmentAppeal), a.Id, null, dryRun,
                () => appeals.RestartReviewWorkflowAsync(a.Id, actorUserId, actorUserName, ct),
                () => InstanceOfAsync(db.RecruitmentAppeals.Where(x => x.Id == a.Id).Select(x => x.WorkflowInstanceId), ct)));

        var stuckChanges = await db.EstablishmentChangeRequests.AsNoTracking()
            .Where(r => r.Status == EstablishmentChangeStatus.PendingApproval && r.WorkflowInstanceId == null)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Id, r.RequestNumber })
            .ToListAsync(ct);
        foreach (var r in stuckChanges)
            items.Add(await RepairAsync("EstablishmentChangeRequest", r.Id, r.RequestNumber, dryRun,
                () => establishment.RestartApprovalAsync(r.Id, actorUserId, actorUserName, ct),
                () => InstanceOfAsync(db.EstablishmentChangeRequests.Where(x => x.Id == r.Id).Select(x => x.WorkflowInstanceId), ct)));

        // G-14: RECRUITMENT-HIRE is the only definition for RecruitmentApplication and has no outcome handler.
        var orphanHires = await db.WorkflowInstances
            .Where(i => i.EntityType == OrphanHireEntityType && i.Status == WorkflowStatus.InProgress)
            .OrderBy(i => i.StartedAt)
            .ToListAsync(ct);
        foreach (var instance in orphanHires)
        {
            if (dryRun)
            {
                items.Add(new ApprovalRepairItem(OrphanHireEntityType, instance.EntityId, null, CancelOrphanAction, Listed, null, instance.Id));
                continue;
            }

            var now = DateTime.UtcNow;
            var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];
            history.Add(new WorkflowStepHistoryEntry
            {
                Level = instance.CurrentStepLevel,
                StepName = "إلغاء (PLAN-25)",
                Status = WorkflowStepStatus.Rejected,
                ActorId = actorUserId,
                ActorName = actorUserName,
                Comments = OrphanHireCancelReason,
                ActionAt = now
            });
            instance.StepHistoryJson = JsonSerializer.Serialize(history);
            instance.Status = WorkflowStatus.Cancelled;
            instance.CompletedAt = now;
            db.AuditLogs.Add(new EntityAuditLog
            {
                EntityType = OrphanHireEntityType,
                EntityId = instance.EntityId,
                Action = OrphanHireCancelAuditAction,
                UserId = actorUserId,
                ChangedFields = JsonSerializer.Serialize(new { workflowInstanceId = instance.Id, reason = OrphanHireCancelReason })
            });
            await db.SaveChangesAsync(ct);
            items.Add(new ApprovalRepairItem(OrphanHireEntityType, instance.EntityId, null, CancelOrphanAction, Repaired, null, instance.Id));
        }
    }

    /// <summary>One row per pending entity of a type: the query is the type's own meaning of «still pending».</summary>
    private sealed record PendingEntity(Guid Id, string? Reference, Guid? LinkedInstanceId);

    private IEnumerable<(string EntityType, IQueryable<PendingEntity> Pending)> PendingEntities() =>
    [
        ("RecruitmentRequisition", db.RecruitmentRequisitions.AsNoTracking()
            .Where(r => r.Status == RequisitionStatus.PendingApproval)
            .Select(r => new PendingEntity(r.Id, r.RequisitionNumber, r.WorkflowInstanceId))),
        ("OfferLetter", db.OfferLetters.AsNoTracking()
            .Where(o => o.Status == OfferStatus.PendingApproval)
            .Select(o => new PendingEntity(o.Id, o.OfferNumber, o.WorkflowInstanceId))),
        ("EstablishmentChangeRequest", db.EstablishmentChangeRequests.AsNoTracking()
            .Where(r => r.Status == EstablishmentChangeStatus.PendingApproval)
            .Select(r => new PendingEntity(r.Id, r.RequestNumber, r.WorkflowInstanceId))),
        ("SelectionCommitteeSession", db.SelectionCommitteeSessions.AsNoTracking()
            .Where(s => s.SubmittedAt != null && s.ApprovedAt == null)
            .Select(s => new PendingEntity(s.Id, null, s.WorkflowInstanceId))),
        (nameof(RecruitmentAppeal), db.RecruitmentAppeals.AsNoTracking()
            .Where(a => a.Status == RecruitmentAppealStatus.Pending)
            .Select(a => new PendingEntity(a.Id, null, a.WorkflowInstanceId))),
        (CompetitionLifecycleService.EntityType, db.RecruitmentCompetitions.AsNoTracking()
            .Where(c => c.Status == RecruitmentCompetitionStatus.PendingApproval)
            .Select(c => new PendingEntity(c.Id, c.Number, c.ApprovalWorkflowInstanceId))),
        (CompetitionResultsService.EntityType, db.CompetitionResultVersions.AsNoTracking()
            .Where(v => v.Status == CompetitionResultVersionStatus.PendingApproval)
            .Select(v => new PendingEntity(v.Id, null, v.WorkflowInstanceId)))
    ];

    /// <returns>How many outcomes stay unapplied after this run, by entity type.</returns>
    private async Task<Dictionary<string, int>> RepairFinishedOutcomesAsync(
        List<ApprovalRepairItem> items, bool dryRun, Guid? actorUserId, CancellationToken ct)
    {
        var unapplied = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entityType, pending) in PendingEntities())
        {
            var entities = await pending.ToListAsync(ct);
            if (entities.Count == 0)
                continue;

            var ids = entities.Select(e => e.Id).ToList();
            var finished = await db.WorkflowInstances.AsNoTracking()
                .Where(i => i.EntityType == entityType && ids.Contains(i.EntityId)
                    && (i.Status == WorkflowStatus.Completed || i.Status == WorkflowStatus.Rejected))
                .OrderBy(i => i.StartedAt)
                .ToListAsync(ct);

            foreach (var instance in finished)
            {
                var entity = entities.First(e => e.Id == instance.EntityId);
                if (entity.LinkedInstanceId != instance.Id)
                {
                    // A rejected instance behind a resubmission is the ordinary history of a request, not a lost outcome.
                    if (instance.Status == WorkflowStatus.Completed)
                        items.Add(new ApprovalRepairItem(
                            entityType, entity.Id, entity.Reference, ApplyOutcomeAction, NotLinked, NotLinkedMessage, instance.Id));
                    continue;
                }

                var item = dryRun
                    ? new ApprovalRepairItem(entityType, entity.Id, entity.Reference, ApplyOutcomeAction, Listed, null, instance.Id)
                    : await ApplyOutcomeAsync(entityType, entity, instance, pending, actorUserId, ct);
                items.Add(item);
                if (item.Result != Repaired)
                    unapplied[entityType] = unapplied.GetValueOrDefault(entityType) + 1;
            }
        }

        return unapplied;
    }

    /// <summary>One transaction per item, so a failing one stops nothing and leaves nothing behind.</summary>
    private async Task<ApprovalRepairItem> ApplyOutcomeAsync(
        string entityType,
        PendingEntity entity,
        WorkflowInstance instance,
        IQueryable<PendingEntity> pending,
        Guid? actorUserId,
        CancellationToken ct)
    {
        var approved = instance.Status == WorkflowStatus.Completed;
        try
        {
            await OutboxUnitOfWork.ExecuteAsync(db, async token =>
            {
                await outcomes.ReapplyOutcomeAsync(instance, approved, LastComment(instance), token);
                db.AuditLogs.Add(new EntityAuditLog
                {
                    EntityType = entityType,
                    EntityId = entity.Id,
                    Action = OutcomeReappliedAuditAction,
                    UserId = actorUserId,
                    ChangedFields = JsonSerializer.Serialize(new { workflowInstanceId = instance.Id, approved })
                });
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            return new ApprovalRepairItem(entityType, entity.Id, entity.Reference, ApplyOutcomeAction, Failed, ex.Message, instance.Id);
        }

        // A projection into a record cannot be filtered in SQL, and the list is short.
        return (await pending.ToListAsync(ct)).Any(e => e.Id == entity.Id && e.LinkedInstanceId == instance.Id)
            ? new ApprovalRepairItem(entityType, entity.Id, entity.Reference, ApplyOutcomeAction, Failed, OutcomeUnchangedMessage, instance.Id)
            : new ApprovalRepairItem(entityType, entity.Id, entity.Reference, ApplyOutcomeAction, Repaired, null, instance.Id);
    }

    private static string? LastComment(WorkflowInstance instance) =>
        JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson)?.LastOrDefault()?.Comments;

    private static async Task<ApprovalRepairItem> RepairAsync(
        string entityType,
        Guid entityId,
        string? reference,
        bool dryRun,
        Func<Task<(bool Success, string? Error)>> repair,
        Func<Task<Guid?>> instanceOf)
    {
        if (dryRun)
            return new ApprovalRepairItem(entityType, entityId, reference, StartWorkflowAction, Listed, null, null);

        // A missing definition (or any refusal) is reported, never thrown, so one bad row does not stop the run.
        var (success, error) = await repair();
        return success
            ? new ApprovalRepairItem(entityType, entityId, reference, StartWorkflowAction, Repaired, null, await instanceOf())
            : new ApprovalRepairItem(entityType, entityId, reference, StartWorkflowAction, Failed, error, null);
    }

    private static Task<Guid?> InstanceOfAsync(IQueryable<Guid?> query, CancellationToken ct) => query.FirstOrDefaultAsync(ct);
}
