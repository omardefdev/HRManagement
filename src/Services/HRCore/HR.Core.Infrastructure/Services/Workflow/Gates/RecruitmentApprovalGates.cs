using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow.Gates;

// PLAN-25 (G-04): the rules below moved from WorkflowInstanceActionService.ValidateSelfApprovalAsync and
// SelectionCommitteeLifecycleService.ApproveStepAsync. Unless noted, a rule applies to approve and reject alike (the
// panels always disabled both buttons for it); the deliberate approve-only rules are marked.

/// <summary>Requisition: the creator never acts on it; branch-scoped actors only inside their scope.</summary>
public sealed class RequisitionApprovalGate(HRCoreDbContext db, WorkflowGateOrgScope scope) : IWorkflowApprovalGate
{
    public const string CreatorMessage = "لا يمكن للمنشئ اعتماد طلبه الخاص.";

    public string EntityType => "RecruitmentRequisition";

    public async Task<string?> GetBlockReasonAsync(WorkflowGateContext context, CancellationToken ct = default)
    {
        var requisition = await db.RecruitmentRequisitions.AsNoTracking()
            .Where(r => r.Id == context.Instance.EntityId)
            .Select(r => new { r.CreatedBy, OrgUnitId = (Guid?)r.FundedPosition!.OrgUnitId })
            .FirstOrDefaultAsync(ct);
        if (requisition is null)
            return null;

        if (context.Actor.UserId is Guid actor && requisition.CreatedBy == actor)
            return CreatorMessage;

        return await scope.GetBlockReasonAsync(context.Actor, requisition.OrgUnitId, ct);
    }
}

/// <summary>
/// Offer: the creator never acts on it; merit-override offers need a distinct approver per step (approve only — a person
/// who approved an earlier step may still reject); branch-scoped actors only inside their scope.
/// </summary>
public sealed class OfferApprovalGate(HRCoreDbContext db, WorkflowGateOrgScope scope) : IWorkflowApprovalGate
{
    public const string CreatorMessage = "لا يمكن للمنشئ اعتماد عرضه الخاص.";

    public string EntityType => "OfferLetter";

    public async Task<string?> GetBlockReasonAsync(WorkflowGateContext context, CancellationToken ct = default)
    {
        var offer = await db.OfferLetters.AsNoTracking()
            .Where(o => o.Id == context.Instance.EntityId)
            .Select(o => new { o.CreatedBy, OrgUnitId = (Guid?)o.FundedPosition!.OrgUnitId })
            .FirstOrDefaultAsync(ct);
        if (offer is null)
            return null;

        var actor = context.Actor.UserId;
        if (actor.HasValue && offer.CreatedBy == actor)
            return CreatorMessage;

        if (context.Kind == WorkflowActionKind.Approve
            && OfferOverrideApprovalRules.IsOverrideWorkflow(context.Definition.Code)
            && OfferOverrideApprovalRules.HasApprovedEarlierStep(
                context.Instance.StepHistoryJson, context.Instance.CurrentStepLevel, actor))
            return OfferOverrideApprovalRules.SameApproverMessage;

        return await scope.GetBlockReasonAsync(context.Actor, offer.OrgUnitId, ct);
    }
}

/// <summary>
/// Selection committee: the session creator never acts on it; a committee member never acts on the final (HR director)
/// step; the quorum step and later need ≥ 3 active members to <b>approve</b> (a committee may be rejected without quorum);
/// branch-scoped actors only inside their scope.
/// </summary>
public sealed class CommitteeApprovalGate(HRCoreDbContext db, WorkflowGateOrgScope scope) : IWorkflowApprovalGate
{
    public string EntityType => "SelectionCommitteeSession";

    public async Task<string?> GetBlockReasonAsync(WorkflowGateContext context, CancellationToken ct = default)
    {
        var session = await db.SelectionCommitteeSessions.AsNoTracking()
            .Where(s => s.Id == context.Instance.EntityId)
            .Select(s => new { s.CreatedBy, OrgUnitId = (Guid?)s.Requisition!.FundedPosition!.OrgUnitId })
            .FirstOrDefaultAsync(ct);
        if (session is null)
            return null;

        var members = await db.SelectionCommitteeMembers.AsNoTracking()
            .Where(m => m.SessionId == context.Instance.EntityId)
            .ToListAsync(ct);
        var level = context.Instance.CurrentStepLevel;

        var rule = context.Kind == WorkflowActionKind.Approve
            ? SelectionCommitteeRules.ValidateApproval(level, members, session.CreatedBy, context.Actor.UserId, context.Actor.EmployeeId)
            : ValidateReject(level, members, session.CreatedBy, context.Actor);
        return rule ?? await scope.GetBlockReasonAsync(context.Actor, session.OrgUnitId, ct);
    }

    private static string? ValidateReject(
        int level,
        IReadOnlyCollection<HR.Core.Domain.Entities.SelectionCommitteeMember> members,
        Guid? createdBy,
        WorkflowActionActor actor)
    {
        if (actor.UserId.HasValue && createdBy == actor.UserId)
            return SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage;
        if (level >= SelectionCommitteeRules.FinalApprovalStepLevel
            && actor.EmployeeId is Guid employeeId
            && members.Any(m => m.EmployeeId == employeeId))
            return SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage;
        return null;
    }
}

/// <summary>
/// Appeal review (HCM-028): only the assigned independent reviewer (with the compliance or admin role) acts; an approve
/// needs the explicit outcome stored first by <c>RecruitmentAppealService.DecideAsync</c> (approve only).
/// </summary>
public sealed class AppealApprovalGate(HRCoreDbContext db) : IWorkflowApprovalGate
{
    public const string ExplicitOutcomeMessage = "يجب البت في الاستئناف بنتيجة صريحة من صفحة الاستئناف.";

    public string EntityType => "RecruitmentAppeal";

    public async Task<string?> GetBlockReasonAsync(WorkflowGateContext context, CancellationToken ct = default)
    {
        // FindAsync sees the outcome DecideAsync set on the tracked appeal before it is saved.
        var appeal = await db.RecruitmentAppeals.FindAsync([context.Instance.EntityId], ct);
        if (appeal is null)
            return null;

        var actor = context.Actor;
        var hasReviewerRole = actor.HasRole(HrManagementRoles.Compliance) || actor.HasRole(HrManagementRoles.Admin);
        if (!RecruitmentAppealWorkflowRules.IsAssignedReviewer(appeal, hasReviewerRole, actor.UserId, actor.EmployeeId))
            return RecruitmentAppealWorkflowRules.NotAssignedReviewerMessage;

        if (context.Kind == WorkflowActionKind.Approve && appeal.Outcome is null)
            return ExplicitOutcomeMessage;

        return null;
    }
}

/// <summary>Competition (HCM-031): the creator never acts on it. Competitions are central-only (PLAN-32).</summary>
public sealed class CompetitionApprovalGate(HRCoreDbContext db, WorkflowGateOrgScope scope) : IWorkflowApprovalGate
{
    public const string CreatorMessage = "لا يمكن للمنشئ اعتماد مسابقته الخاصة.";

    public string EntityType => CompetitionLifecycleService.EntityType;

    public async Task<string?> GetBlockReasonAsync(WorkflowGateContext context, CancellationToken ct = default)
    {
        var createdBy = await db.RecruitmentCompetitions.AsNoTracking()
            .Where(c => c.Id == context.Instance.EntityId)
            .Select(c => c.CreatedBy)
            .FirstOrDefaultAsync(ct);
        if (context.Actor.UserId is Guid actor && createdBy == actor)
            return CreatorMessage;

        return await scope.GetBlockReasonAsync(context.Actor, entityOrgUnitId: null, ct);
    }
}

/// <summary>
/// Competition results (HCM-031 J5): never the person who computed (or created) the draft, who submitted it, or who decided
/// a tie on this computation; a distinct approver per step (approve only). Central-only.
/// </summary>
public sealed class ResultsApprovalGate(HRCoreDbContext db, WorkflowGateOrgScope scope) : IWorkflowApprovalGate
{
    public const string ComputedByMessage = "لا يجوز لمن احتسب النتائج اعتمادها.";
    public const string SubmitterMessage = "لا يجوز لمن قدّم النتائج للاعتماد اعتمادها.";
    public const string TieResolverMessage = "لا يجوز لمن بتّ في التعادل اعتماد النتائج.";
    public const string DistinctApproverMessage = "يجب أن يعتمد كل خطوة من خطوات اعتماد النتائج شخص مختلف.";

    public string EntityType => CompetitionResultsService.EntityType;

    public async Task<string?> GetBlockReasonAsync(WorkflowGateContext context, CancellationToken ct = default)
    {
        var instance = context.Instance;
        if (context.Actor.UserId is Guid actor)
        {
            var version = await db.CompetitionResultVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == instance.EntityId, ct);
            if (version?.ComputedBy == actor || version?.CreatedBy == actor)
                return ComputedByMessage;
            if (instance.InitiatedBy == actor)
                return SubmitterMessage;
            if (version is not null && await db.AuditLogs.AsNoTracking().AnyAsync(a =>
                    a.EntityType == CompetitionLifecycleService.EntityType && a.EntityId == version.CompetitionId
                    && a.Action == CompetitionResultsService.ResolveTieAuditAction && a.UserId == actor
                    && a.CreatedAt >= version.ComputedAt, ct))
                return TieResolverMessage;
            if (context.Kind == WorkflowActionKind.Approve
                && OfferOverrideApprovalRules.HasApprovedEarlierStep(instance.StepHistoryJson, instance.CurrentStepLevel, actor))
                return DistinctApproverMessage;
        }

        return await scope.GetBlockReasonAsync(context.Actor, entityOrgUnitId: null, ct);
    }
}

/// <summary>Establishment change (EST-002): the creator never acts on it; central HR only (no branch scope).</summary>
public sealed class EstablishmentChangeApprovalGate(HRCoreDbContext db, WorkflowGateOrgScope scope) : IWorkflowApprovalGate
{
    public const string CreatorMessage = "لا يمكن للمنشئ اعتماد طلبه الخاص.";

    public string EntityType => "EstablishmentChangeRequest";

    public async Task<string?> GetBlockReasonAsync(WorkflowGateContext context, CancellationToken ct = default)
    {
        var createdBy = await db.EstablishmentChangeRequests.AsNoTracking()
            .Where(r => r.Id == context.Instance.EntityId)
            .Select(r => r.CreatedBy)
            .FirstOrDefaultAsync(ct);
        if (context.Actor.UserId is Guid actor && createdBy == actor)
            return CreatorMessage;

        return await scope.GetBlockReasonAsync(context.Actor, entityOrgUnitId: null, ct);
    }
}
