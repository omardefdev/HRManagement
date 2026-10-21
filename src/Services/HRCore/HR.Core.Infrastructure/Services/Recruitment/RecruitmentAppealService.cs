using System.Text.Json;
using HR.Core.Domain.Entities;
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

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class RecruitmentAppealService(
    HRCoreDbContext db,
    IPolicyOrchestrator policyOrchestrator,
    IWorkflowStarter workflowStarter,
    IConfiguration configuration)
{
    private const string AppealReviewWorkflowCode = "RECRUITMENT-APPEAL-REVIEW";

    public async Task<(RecruitmentAppeal? Appeal, string? Error)> FileAppealAsync(
        Guid applicationId,
        string reason,
        Guid independentReviewerId,
        Guid? actorUserId = null,
        string? actorUserName = null,
        CancellationToken ct = default)
    {
        var app = await db.RecruitmentApplications.FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null) return (null, "الطلب غير موجود.");

        if (!RecruitmentAppealWorkflowRules.CanAppeal(app, DateTime.UtcNow))
            return (null, "لا يمكن تقديم الاستئناف في الحالة الحالية.");

        if (app.RequisitionId is Guid reqId)
        {
            var session = await db.SelectionCommitteeSessions
                .Include(s => s.Members)
                .Where(s => s.RequisitionId == reqId)
                .OrderByDescending(s => s.CommitteeDate)
                .FirstOrDefaultAsync(ct);

            if (session is not null
                && !RecruitmentAppealWorkflowRules.IsIndependentReviewer(session.Members, independentReviewerId))
                return (null, "يجب تعيين مراجع مستقل غير عضو في لجنة الاختيار.");
        }

        var appeal = new RecruitmentAppeal
        {
            RecruitmentApplicationId = app.Id,
            Reason = reason.Trim(),
            Status = RecruitmentAppealStatus.Pending,
            IndependentReviewerId = independentReviewerId,
            CreatedBy = actorUserId
        };
        db.RecruitmentAppeals.Add(appeal);
        app.AppealStatus = RecruitmentAppealStatus.Pending;
        app.AppealNotes = reason.Trim();
        await db.SaveChangesAsync(ct);

        var contextJson = JsonSerializer.Serialize(new
        {
            appealId = appeal.Id,
            recruitmentApplicationId = app.Id,
            independentReviewerId
        });

        var mode = ResolveMode();
        if (mode == PolicyEnforcementMode.Off)
        {
            if (IsLabScale())
            {
                appeal.WorkflowInstanceId = await workflowStarter.StartByWorkflowCodeAsync(
                    AppealReviewWorkflowCode,
                    "RecruitmentAppeal",
                    appeal.Id,
                    appeal.TenantId,
                    actorUserId,
                    actorUserName,
                    contextJson,
                    PolicyDomains.CoreRecruitment,
                    ct);
                await db.SaveChangesAsync(ct);
            }

            return (appeal, null);
        }

        var policyRequest = PolicyDomainFactsBuilders.ForGeneric(
            PolicyDomains.CoreRecruitment,
            "RecruitmentAppeal",
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

        appeal.WorkflowInstanceId = policyResult.WorkflowInstanceId;
        await db.SaveChangesAsync(ct);
        return (appeal, null);
    }

    public async Task<(bool Success, string? Error)> DecideAppealAsync(
        Guid appealId,
        bool upheld,
        string decision,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var appeal = await db.RecruitmentAppeals
            .Include(a => a.RecruitmentApplication)
            .FirstOrDefaultAsync(a => a.Id == appealId, ct);
        if (appeal is null) return (false, "الاستئناف غير موجود.");

        if (appeal.WorkflowInstanceId is not null)
        {
            var active = await db.WorkflowInstances.AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == appeal.WorkflowInstanceId, ct);
            if (active is not null && active.Status == WorkflowStatus.InProgress)
                return (false, "يجب البت في الاستئناف عبر مسار الاعتماد.");
        }

        if (!RecruitmentAppealWorkflowRules.CanDecideAppeal(appeal, actorUserId))
            return (false, "غير مصرح باتخاذ قرار الاستئناف.");

        appeal.Status = RecruitmentAppealStatus.Decided;
        appeal.DecidedAt = DateTime.UtcNow;
        appeal.Decision = decision.Trim();

        var app = appeal.RecruitmentApplication!;
        app.AppealStatus = RecruitmentAppealStatus.Decided;
        app.Status = upheld ? RecruitmentApplicationStatus.UnderReview : RecruitmentApplicationStatus.Rejected;
        app.ReviewedAt = DateTime.UtcNow;

        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(RecruitmentAppeal),
            EntityId = appeal.Id,
            Action = upheld ? "AppealUpheld" : "AppealRejected",
            UserId = actorUserId,
            ChangedFields = $$"""{"decision":{{JsonSerializer.Serialize(decision)}}}"""
        });

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    private PolicyEnforcementMode ResolveMode()
    {
        var raw = configuration["PolicyEngine:Core.Recruitment"]
            ?? configuration["PolicyEngine:DefaultMode"]
            ?? nameof(PolicyEnforcementMode.Off);
        return Enum.TryParse<PolicyEnforcementMode>(raw, true, out var mode)
            ? mode
            : PolicyEnforcementMode.Off;
    }

    private bool IsLabScale()
    {
        var value = configuration["NationalScope:IsLabScale"];
        return bool.TryParse(value, out var parsed) && parsed;
    }
}
