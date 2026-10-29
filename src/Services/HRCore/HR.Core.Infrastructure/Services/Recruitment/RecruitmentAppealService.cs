using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-028 (H2/H3): who acts on an appeal review. <see cref="HasReviewerRole"/> is the compliance role (or Admin);
/// <see cref="EmployeeId"/> comes from the <c>employee_id</c> claim.
/// </summary>
public sealed record AppealReviewActor(WorkflowActionActor Workflow, Guid? EmployeeId, bool HasReviewerRole);

/// <summary>
/// HCM-006 / HCM-028 (G-09): the single appeal path — HR files on the candidate's behalf, HR assigns an independent
/// reviewer (which starts RECRUITMENT-APPEAL-REVIEW), the assigned compliance reviewer decides an explicit outcome or
/// asks for more information, and HR resubmits.
/// </summary>
public sealed class RecruitmentAppealService(
    HRCoreDbContext db,
    IPolicyOrchestrator policyOrchestrator,
    IWorkflowStarter workflowStarter,
    IConfiguration configuration,
    IOptions<RecruitmentOptions> options,
    IWorkflowInstanceActionService workflowActions,
    SelectionCommitteeService committeeService,
    RecruitmentAppealWorkflowHandler appealHandler)
{
    public const string AppealReviewWorkflowCode = "RECRUITMENT-APPEAL-REVIEW";
    public const string NotFoundMessage = "الاستئناف غير موجود.";
    public const string NotPendingMessage = "الاستئناف مُبتّ فيه.";
    public const string AwaitingInfoMessage = "بانتظار استيفاء المعلومات المطلوبة قبل البت.";
    public const string NoActiveWorkflowMessage = "لا يوجد مسار مراجعة نشط لهذا الاستئناف.";

    /// <summary>HCM-028 (G3): null when the application can be appealed now, else the reason.</summary>
    public string? GetFilingError(RecruitmentApplication application, DateTime utcNow) =>
        RecruitmentAppealWorkflowRules.GetAppealError(
            application, utcNow, options.Value.AppealWindowDays, options.Value.GetAppealableRejectionKinds());

    /// <summary>HCM-028 (H1): files the appeal without a reviewer; the review workflow starts at assignment.</summary>
    public async Task<(RecruitmentAppeal? Appeal, string? Error)> FileAppealAsync(
        Guid applicationId,
        string? reason,
        string filedBy,
        Guid? actorUserId,
        Guid? actorEmployeeId = null,
        CancellationToken ct = default)
    {
        // HCM-030 (PLAN-30 I): the candidate files on /careers/my (FiledBy = Candidate, no user, CandidatePortalService);
        // HR files on the candidate's behalf (HrOnBehalf).
        if (!RecruitmentAppealFiledBy.IsValid(filedBy))
            return (null, "جهة تقديم الاستئناف غير صالحة.");

        var trimmed = reason?.Trim();
        if (trimmed is null || trimmed.Length < RecruitmentAppealWorkflowRules.MinReasonLength)
            return (null, $"سبب الاستئناف مطلوب ({RecruitmentAppealWorkflowRules.MinReasonLength} حرفاً على الأقل).");
        if (trimmed.Length > 2000)
            return (null, "سبب الاستئناف يجب ألا يتجاوز 2000 حرف.");

        var app = await db.RecruitmentApplications.FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null) return (null, "الطلب غير موجود.");

        var error = GetFilingError(app, DateTime.UtcNow);
        if (error is not null)
            return (null, error);

        var appeal = new RecruitmentAppeal
        {
            RecruitmentApplicationId = app.Id,
            Reason = trimmed,
            Status = RecruitmentAppealStatus.Pending,
            FiledBy = filedBy,
            FiledByEmployeeId = actorEmployeeId,
            CreatedBy = actorUserId
        };
        db.RecruitmentAppeals.Add(appeal);
        app.AppealStatus = RecruitmentAppealStatus.Pending;
        AddAudit(appeal.Id, "AppealFiled", actorUserId, new
        {
            applicationId = app.Id,
            filedBy,
            rejectionKind = RecruitmentAppealWorkflowRules.EffectiveRejectionKind(app).ToString(),
            rejectedAt = app.RejectedAt ?? app.ReviewedAt
        });

        await db.SaveChangesAsync(ct);
        return (appeal, null);
    }

    /// <summary>
    /// HCM-028 (H2): assigns (or, while pending, reassigns) the independent reviewer and starts the review workflow.
    /// A reassignment cancels the in-progress workflow instance.
    /// </summary>
    public async Task<(bool Success, string? Error)> AssignReviewerAsync(
        Guid appealId,
        Guid reviewerEmployeeId,
        Guid? actorUserId,
        Guid? actorEmployeeId = null,
        string? actorUserName = null,
        CancellationToken ct = default)
    {
        var appeal = await LoadAppealAsync(appealId, ct);
        if (appeal is null) return (false, NotFoundMessage);
        if (appeal.Status != RecruitmentAppealStatus.Pending) return (false, NotPendingMessage);
        if (appeal.IndependentReviewerEmployeeId == reviewerEmployeeId)
            return (false, "المراجع المحدد معيّن مسبقاً لهذا الاستئناف.");

        var reviewerExists = await db.Employees.AnyAsync(e => e.Id == reviewerEmployeeId, ct);
        if (!reviewerExists) return (false, "الموظف المحدد غير موجود.");

        var excluded = await GetNonIndependentEmployeeIdsAsync(appeal, actorEmployeeId, ct);
        if (excluded.Contains(reviewerEmployeeId))
            return (false, RecruitmentAppealWorkflowRules.NotIndependentMessage);

        var now = DateTime.UtcNow;
        var previousReviewer = appeal.IndependentReviewerEmployeeId;
        var previousWorkflow = appeal.WorkflowInstanceId;
        if (previousWorkflow is Guid previousId)
        {
            var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == previousId, ct);
            if (instance is { Status: WorkflowStatus.InProgress })
            {
                instance.Status = WorkflowStatus.Cancelled;
                instance.CompletedAt = now;
            }
        }

        var reassignment = previousReviewer is not null || previousWorkflow is not null || appeal.AssignedAt is not null;
        appeal.IndependentReviewerEmployeeId = reviewerEmployeeId;
        appeal.IndependentReviewerId = null;
        appeal.AssignedAt = now;
        appeal.AssignedBy = actorUserId;
        appeal.WorkflowInstanceId = null;
        AddAudit(appeal.Id, reassignment ? "AppealReviewerReassigned" : "AppealReviewerAssigned", actorUserId, new
        {
            reviewerEmployeeId,
            previousReviewerEmployeeId = previousReviewer,
            cancelledWorkflowInstanceId = previousWorkflow
        });

        var (_, startError) = await StartReviewWorkflowAsync(appeal, actorUserId, actorUserName, ct);
        if (startError is not null)
        {
            await DiscardChangesAsync(ct);
            return (false, startError);
        }

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// PLAN-25 (G-02 repair): starts RECRUITMENT-APPEAL-REVIEW for a pending appeal that has an assigned reviewer, is not
    /// waiting for information and has no instance. A missing definition is reported as an error (H5 still lets the
    /// reviewer decide directly).
    /// </summary>
    public async Task<(bool Success, string? Error)> RestartReviewWorkflowAsync(
        Guid appealId,
        Guid? actorUserId,
        string? actorUserName = null,
        CancellationToken ct = default)
    {
        var appeal = await LoadAppealAsync(appealId, ct);
        if (appeal is null) return (false, NotFoundMessage);
        if (appeal.Status != RecruitmentAppealStatus.Pending
            || appeal.WorkflowInstanceId is not null
            || appeal.IsAwaitingInfo
            || (appeal.IndependentReviewerEmployeeId is null && appeal.IndependentReviewerId is null))
            return (false, ApprovalRestartRules.NotStuckMessage);

        var (instanceId, startError) = await StartReviewWorkflowAsync(appeal, actorUserId, actorUserName, ct);
        if (startError is not null || instanceId is not Guid started)
        {
            await DiscardChangesAsync(ct);
            return (false, startError ?? ApprovalStartCoordinator.MissingDefinitionMessage);
        }

        ApprovalRestartRules.AddRestartAudit(db, nameof(RecruitmentAppeal), appeal.Id, started, actorUserId);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// HCM-028 (H3): the assigned reviewer's explicit outcome. The outcome is stored, then the review step is approved;
    /// <see cref="RecruitmentAppealWorkflowHandler"/> applies its effect. Without a workflow instance (one could not be
    /// started — H5) the decision is applied directly under the same checks and audited as <c>AppealDecidedWithoutWorkflow</c>.
    /// </summary>
    public async Task<(bool Success, string? Error)> DecideAsync(
        Guid appealId,
        RecruitmentAppealOutcome outcome,
        string? rationale,
        int? correctedRank,
        AppealReviewActor actor,
        CancellationToken ct = default)
    {
        var appeal = await LoadAppealAsync(appealId, ct);
        if (appeal is null) return (false, NotFoundMessage);

        var (instance, accessError) = await ValidateReviewerActionAsync(appeal, actor, ct);
        if (accessError is not null) return (false, accessError);

        var trimmed = rationale?.Trim();
        if (trimmed is null || trimmed.Length < RecruitmentAppealWorkflowRules.MinRationaleLength)
            return (false, $"التعليل مطلوب ({RecruitmentAppealWorkflowRules.MinRationaleLength} حرفاً على الأقل).");
        if (trimmed.Length > 2000)
            return (false, "التعليل يجب ألا يتجاوز 2000 حرف.");

        var app = appeal.RecruitmentApplication!;
        var outcomeError = appeal.Kind == RecruitmentAppealKind.ResultObjection
            ? RecruitmentAppealWorkflowRules.ValidateObjectionOutcome(outcome, appeal.ObjectionCorrectedScore)
            : RecruitmentAppealWorkflowRules.ValidateOutcome(
                outcome, app.Requisition?.Status, app.RankBeforeRejection ?? app.Rank, correctedRank);
        if (outcomeError is not null) return (false, outcomeError);

        if (outcome == RecruitmentAppealOutcome.UpheldRankCorrection)
        {
            var rankError = await new RecruitmentSelectionService(db).ValidateAppealRankCorrectionAsync(
                app.Id, app.RequisitionId, correctedRank!.Value, ct);
            if (rankError is not null) return (false, rankError);
        }

        appeal.Outcome = outcome;
        appeal.Decision = trimmed;
        appeal.CorrectedRank = outcome == RecruitmentAppealOutcome.UpheldRankCorrection ? correctedRank : null;

        if (instance is not null)
        {
            var result = await workflowActions.ApproveAsync(instance.Id, WorkflowActor(actor), trimmed, targetStepLevel: null, ct);
            if (!result.Success)
            {
                await DiscardChangesAsync(ct);
                return (false, result.Error ?? "تعذّر اعتماد قرار الاستئناف.");
            }

            return (true, null);
        }

        await appealHandler.ApplyOutcomeAsync(appeal, actor.Workflow.UserId, workflowInstanceId: null, ct);
        AddAudit(appeal.Id, "AppealDecidedWithoutWorkflow", actor.Workflow.UserId, new { outcome = outcome.ToString() });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>HCM-028 (H3): the reviewer rejects the review step with a question; the appeal stays pending.</summary>
    public async Task<(bool Success, string? Error)> RequestInfoAsync(
        Guid appealId,
        string? text,
        AppealReviewActor actor,
        CancellationToken ct = default)
    {
        var appeal = await LoadAppealAsync(appealId, ct);
        if (appeal is null) return (false, NotFoundMessage);

        var (instance, accessError) = await ValidateReviewerActionAsync(appeal, actor, ct);
        if (accessError is not null) return (false, accessError);

        var trimmed = text?.Trim();
        if (trimmed is null || trimmed.Length < RecruitmentAppealWorkflowRules.MinInfoTextLength)
            return (false, $"نص طلب المعلومات مطلوب ({RecruitmentAppealWorkflowRules.MinInfoTextLength} أحرف على الأقل).");
        if (trimmed.Length > 2000)
            return (false, "نص طلب المعلومات يجب ألا يتجاوز 2000 حرف.");

        if (instance is not null)
        {
            var result = await workflowActions.RejectAsync(instance.Id, WorkflowActor(actor), trimmed, ct);
            return result.Success ? (true, null) : (false, result.Error ?? "تعذّر طلب المعلومات.");
        }

        appealHandler.RecordInfoRequest(appeal, trimmed, actor.Workflow.UserId, workflowInstanceId: null);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>HCM-028 (H3): HR supplies the requested information; a new review workflow starts for the same reviewer.</summary>
    public async Task<(bool Success, string? Error)> ResubmitAsync(
        Guid appealId,
        string? additionalInfo,
        Guid? actorUserId,
        string? actorUserName = null,
        CancellationToken ct = default)
    {
        var appeal = await LoadAppealAsync(appealId, ct);
        if (appeal is null) return (false, NotFoundMessage);
        if (appeal.Status != RecruitmentAppealStatus.Pending) return (false, NotPendingMessage);
        if (!appeal.IsAwaitingInfo) return (false, "لا يوجد طلب معلومات بانتظار الاستيفاء.");

        var trimmed = additionalInfo?.Trim();
        if (trimmed is null || trimmed.Length < RecruitmentAppealWorkflowRules.MinInfoTextLength)
            return (false, $"المعلومات الإضافية مطلوبة ({RecruitmentAppealWorkflowRules.MinInfoTextLength} أحرف على الأقل).");
        if (trimmed.Length > 4000)
            return (false, "المعلومات الإضافية يجب ألا تتجاوز 4000 حرف.");

        appeal.AdditionalInfo = trimmed;
        appeal.AdditionalInfoAt = DateTime.UtcNow;
        AddAudit(appeal.Id, "AppealResubmitted", actorUserId, new { additionalInfo = trimmed });

        var (_, startError) = await StartReviewWorkflowAsync(appeal, actorUserId, actorUserName, ct);
        if (startError is not null)
        {
            await DiscardChangesAsync(ct);
            return (false, startError);
        }

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// HCM-028 (H2): employees who cannot review the appeal — the applicant (internal candidate), every committee member
    /// of the requisition, the interview panel and the assessors of the application, the filer and the assigner.
    /// </summary>
    public async Task<IReadOnlySet<Guid>> GetNonIndependentEmployeeIdsAsync(
        RecruitmentAppeal appeal,
        Guid? assignerEmployeeId,
        CancellationToken ct = default)
    {
        var app = appeal.RecruitmentApplication
            ?? await db.RecruitmentApplications.AsNoTracking().FirstAsync(a => a.Id == appeal.RecruitmentApplicationId, ct);
        var excluded = new HashSet<Guid>();

        if (app.SourceInternalApplicationId is Guid internalId)
        {
            var applicantEmployee = await db.InternalApplications.AsNoTracking()
                .Where(i => i.Id == internalId)
                .Select(i => (Guid?)i.EmployeeId)
                .FirstOrDefaultAsync(ct);
            if (applicantEmployee is Guid applicantId) excluded.Add(applicantId);
        }

        if (app.HiredEmployeeId is Guid hiredId) excluded.Add(hiredId);

        if (app.RequisitionId is Guid requisitionId)
            excluded.UnionWith(await committeeService.GetMemberEmployeeIdsAsync(requisitionId, ct));

        excluded.UnionWith(await db.RecruitmentInterviewPanelMembers.AsNoTracking()
            .Where(m => m.Slot != null && m.Slot.RecruitmentApplicationId == app.Id)
            .Select(m => m.EmployeeId)
            .ToListAsync(ct));

        excluded.UnionWith(await db.RecruitmentAssessmentScores.AsNoTracking()
            .Where(s => s.RecruitmentApplicationId == app.Id && s.AssessorEmployeeId != null)
            .Select(s => s.AssessorEmployeeId!.Value)
            .ToListAsync(ct));

        // HCM-031 (PLAN-31): a result objection is not reviewed by a marker of the competition's exam sessions.
        if (app.CompetitionId is Guid competitionId)
            excluded.UnionWith(await db.ExamMarkerAssignments.AsNoTracking()
                .Where(m => m.ExamSession!.CompetitionId == competitionId && m.MarkerEmployeeId != null)
                .Select(m => m.MarkerEmployeeId!.Value)
                .ToListAsync(ct));

        if (appeal.FiledByEmployeeId is Guid filerId) excluded.Add(filerId);
        if (assignerEmployeeId is Guid assignerId) excluded.Add(assignerId);
        return excluded;
    }

    /// <summary>Authorization and state shared by decide and request-info; returns the in-progress instance, if any.</summary>
    private async Task<(WorkflowInstance? Instance, string? Error)> ValidateReviewerActionAsync(
        RecruitmentAppeal appeal,
        AppealReviewActor actor,
        CancellationToken ct)
    {
        if (!RecruitmentAppealWorkflowRules.IsAssignedReviewer(
                appeal, actor.HasReviewerRole, actor.Workflow.UserId, actor.EmployeeId))
            return (null, RecruitmentAppealWorkflowRules.NotAssignedReviewerMessage);

        if (appeal.WorkflowInstanceId is Guid instanceId)
        {
            var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
            return instance is { Status: WorkflowStatus.InProgress }
                ? (instance, null)
                : (null, NoActiveWorkflowMessage);
        }

        // No instance: either the reviewer asked for information, or no workflow could be started (H5).
        return appeal.IsAwaitingInfo ? (null, AwaitingInfoMessage) : (null, null);
    }

    /// <summary>
    /// Starts RECRUITMENT-APPEAL-REVIEW for the assigned reviewer (stages <c>WorkflowInstanceId</c>). A null instance
    /// without an error means no workflow definition could be resolved; the reviewer then decides directly (H5).
    /// </summary>
    private async Task<(Guid? InstanceId, string? Error)> StartReviewWorkflowAsync(
        RecruitmentAppeal appeal,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct)
    {
        var contextJson = JsonSerializer.Serialize(new
        {
            appealId = appeal.Id,
            recruitmentApplicationId = appeal.RecruitmentApplicationId,
            independentReviewerEmployeeId = appeal.IndependentReviewerEmployeeId
        });

        Guid? instanceId = null;
        var mode = ResolveMode();
        if (mode != PolicyEnforcementMode.Off)
        {
            var policyRequest = PolicyDomainFactsBuilders.ForGeneric(
                PolicyDomains.CoreRecruitment,
                "RecruitmentAppeal",
                // The seeded Core.Recruitment rule matches this action name; the review now starts at assignment.
                "FileAppeal",
                appeal.Id,
                appeal.TenantId);
            policyRequest.ContextJson = contextJson;
            policyRequest.Mode = mode;
            policyRequest.ActorUserId = actorUserId;
            policyRequest.ActorUserName = actorUserName;

            var policyResult = await policyOrchestrator.EnforceAsync(policyRequest, ct);
            if (!policyResult.Allowed)
                return (null, policyResult.Decision.DenyReason ?? "مرفوض بسياسة الاستئناف.");
            instanceId = policyResult.WorkflowInstanceId;
        }

        // PLAN-25: always start workflow — HCM-028 (H5) already does so for policy Off outside lab scale, and when the
        // policy did not start one, so the reviewer always decides through RECRUITMENT-APPEAL-REVIEW when it exists.
        instanceId ??= await workflowStarter.StartByWorkflowCodeAsync(
            AppealReviewWorkflowCode,
            "RecruitmentAppeal",
            appeal.Id,
            appeal.TenantId,
            actorUserId,
            actorUserName,
            contextJson,
            PolicyDomains.CoreRecruitment,
            ct);

        appeal.WorkflowInstanceId = instanceId;
        return (instanceId, null);
    }

    /// <summary>PLAN-25: the appeal gate identifies the reviewer by employee id, carried on the workflow actor.</summary>
    private static WorkflowActionActor WorkflowActor(AppealReviewActor actor) =>
        actor.Workflow with { EmployeeId = actor.Workflow.EmployeeId ?? actor.EmployeeId };

    private Task<RecruitmentAppeal?> LoadAppealAsync(Guid appealId, CancellationToken ct) =>
        db.RecruitmentAppeals
            .Include(a => a.RecruitmentApplication!)
                .ThenInclude(app => app.Requisition)
            .FirstOrDefaultAsync(a => a.Id == appealId, ct);

    /// <summary>Drops staged changes after a refused step (the controller does not save; tests reuse the context).</summary>
    private async Task DiscardChangesAsync(CancellationToken ct)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    await entry.ReloadAsync(ct);
                    break;
            }
        }
    }

    private void AddAudit(Guid appealId, string action, Guid? actorUserId, object changes) =>
        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(RecruitmentAppeal),
            EntityId = appealId,
            Action = action,
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(changes)
        });

    private PolicyEnforcementMode ResolveMode()
    {
        var raw = configuration["PolicyEngine:Core.Recruitment"]
            ?? configuration["PolicyEngine:DefaultMode"]
            ?? nameof(PolicyEnforcementMode.Off);
        return Enum.TryParse<PolicyEnforcementMode>(raw, true, out var mode)
            ? mode
            : PolicyEnforcementMode.Off;
    }
}
