using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
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

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class SelectionCommitteeLifecycleService(
    HRCoreDbContext db,
    IPolicyOrchestrator policyOrchestrator,
    IWorkflowStarter workflowStarter,
    IConfiguration configuration,
    SelectionCommitteeService committeeService,
    IWorkflowInstanceActionService workflowActions)
{
    private readonly ApprovalStartCoordinator approvalStart = new(db, policyOrchestrator, workflowStarter, configuration);
    private const string CommitteeWorkflowCode = "RECRUITMENT-SELECTION-COMMITTEE";
    public const string NoWorkflowMessage = "لا يوجد مسار اعتماد مرتبط بهذه الجلسة.";

    /// <summary>
    /// HCM-028: validates the session (quorum, chair, a supported decision per candidate, proposed ranks),
    /// locks it and starts the committee workflow.
    /// </summary>
    public async Task<(bool Success, string? Error)> SubmitForApprovalAsync(
        Guid sessionId,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct = default)
    {
        var session = await db.SelectionCommitteeSessions
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return (false, "جلسة اللجنة غير موجودة.");
        if (session.ApprovedAt is not null) return (false, "تم اعتماد هذه الجلسة مسبقاً.");
        if (session.WorkflowInstanceId is not null || session.SubmittedAt is not null)
            return (false, "الجلسة قيد الاعتماد بالفعل.");
        if (!session.Members.Any()) return (false, "يجب إضافة عضو واحد على الأقل قبل التقديم.");

        var workspace = await committeeService.GetWorkspaceAsync(sessionId, ct);
        var submitError = workspace?.SubmitIssues.FirstOrDefault();
        if (submitError is not null) return (false, submitError);

        var contextJson = BuildApprovalContextJson(session);

        // PLAN-25 (G-02): the session is submitted and locked only together with its workflow instance.
        var start = await approvalStart.StartAndApplyAsync(
            new ApprovalStartRequest(
                "SelectionCommitteeSession",
                session.Id,
                session.TenantId,
                CommitteeWorkflowCode,
                PolicyDomains.CoreRecruitment,
                actorUserId,
                actorUserName,
                contextJson,
                () =>
                {
                    var policyRequest = PolicyDomainFactsBuilders.ForGeneric(
                        PolicyDomains.CoreRecruitment,
                        "SelectionCommitteeSession",
                        "SubmitCommittee",
                        session.Id,
                        session.TenantId);
                    policyRequest.ContextJson = contextJson;
                    return policyRequest;
                },
                "مرفوض بسياسة اللجنة."),
            async instanceId =>
            {
                var now = DateTime.UtcNow;
                session.SubmittedAt = now;
                session.LockedAt = now;
                session.WorkflowInstanceId = instanceId;
                db.AuditLogs.Add(new EntityAuditLog
                {
                    EntityType = nameof(SelectionCommitteeSession),
                    EntityId = session.Id,
                    Action = "CommitteeSubmitted",
                    UserId = actorUserId,
                    ChangedFields = JsonSerializer.Serialize(new { decisions = workspace!.Decisions.Count })
                });
                await db.SaveChangesAsync(ct);
            },
            ct);

        return start.Started ? (true, null) : (false, start.Error);
    }

    /// <summary>PLAN-25: starts the missing workflow of a session that was submitted (locked) without one.</summary>
    public async Task<(bool Success, string? Error)> RestartApprovalAsync(
        Guid sessionId,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct = default)
    {
        var session = await db.SelectionCommitteeSessions
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return (false, "جلسة اللجنة غير موجودة.");
        if (session.SubmittedAt is null || session.ApprovedAt is not null || session.WorkflowInstanceId is not null)
            return (false, ApprovalRestartRules.NotStuckMessage);

        var start = await approvalStart.StartAndApplyAsync(
            new ApprovalStartRequest(
                "SelectionCommitteeSession",
                session.Id,
                session.TenantId,
                CommitteeWorkflowCode,
                PolicyDomains.CoreRecruitment,
                actorUserId,
                actorUserName,
                BuildApprovalContextJson(session)),
            async instanceId =>
            {
                session.WorkflowInstanceId = instanceId;
                ApprovalRestartRules.AddRestartAudit(db, nameof(SelectionCommitteeSession), session.Id, instanceId, actorUserId);
                await db.SaveChangesAsync(ct);
            },
            ct);

        return start.Started ? (true, null) : (false, start.Error);
    }

    private static string BuildApprovalContextJson(SelectionCommitteeSession session) => JsonSerializer.Serialize(new
    {
        sessionId = session.Id,
        requisitionId = session.RequisitionId,
        memberCount = session.Members.Count(m => !m.Recused)
    });

    /// <summary>
    /// HCM-028 (B): server-side committee checks before a workflow step is approved — quorum from the DB at
    /// step ≥ 2, and neither the session creator nor (at step 3) a member may approve.
    /// </summary>
    public async Task<(bool Success, string? Error)> ApproveStepAsync(
        Guid sessionId,
        WorkflowActionActor actor,
        Guid? actorEmployeeId,
        string? comments,
        CancellationToken ct = default)
    {
        var (session, instance, error) = await LoadForActionAsync(sessionId, ct);
        if (error is not null) return (false, error);

        var members = await db.SelectionCommitteeMembers.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .ToListAsync(ct);
        var sodError = SelectionCommitteeRules.ValidateApproval(
            instance!.CurrentStepLevel, members, session!.CreatedBy, actor.UserId, actorEmployeeId);
        if (sodError is not null) return (false, sodError);

        var result = await workflowActions.ApproveAsync(instance.Id, actor, comments, instance.CurrentStepLevel, ct);
        return result.Success ? (true, null) : (false, result.Error ?? "تعذّر اعتماد اللجنة.");
    }

    /// <summary>HCM-028 (B): rejecting returns the session to the secretary unlocked (see the workflow handler).</summary>
    public async Task<(bool Success, string? Error)> RejectStepAsync(
        Guid sessionId,
        WorkflowActionActor actor,
        Guid? actorEmployeeId,
        string? comments,
        CancellationToken ct = default)
    {
        var (_, instance, error) = await LoadForActionAsync(sessionId, ct);
        if (error is not null) return (false, error);

        var result = await workflowActions.RejectAsync(instance!.Id, actor, comments, ct);
        return result.Success ? (true, null) : (false, result.Error ?? "تعذّر رفض اللجنة.");
    }

    private async Task<(SelectionCommitteeSession? Session, WorkflowInstance? Instance, string? Error)>
        LoadForActionAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.SelectionCommitteeSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return (null, null, "جلسة اللجنة غير موجودة.");
        if (session.ApprovedAt is not null) return (null, null, "تم اعتماد هذه الجلسة مسبقاً.");
        if (session.WorkflowInstanceId is not Guid workflowId) return (null, null, NoWorkflowMessage);
        if (!session.IsLocked) return (null, null, "الجلسة غير مقدّمة للاعتماد.");

        var instance = await db.WorkflowInstances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == workflowId, ct);
        if (instance is null) return (null, null, NoWorkflowMessage);
        if (instance.Status != WorkflowStatus.InProgress)
            return (null, null, "مسار الاعتماد ليس قيد التنفيذ.");

        return (session, instance, null);
    }
}
