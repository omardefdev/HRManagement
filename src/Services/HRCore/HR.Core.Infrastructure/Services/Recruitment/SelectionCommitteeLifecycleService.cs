using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class SelectionCommitteeLifecycleService(
    HRCoreDbContext db,
    IPolicyOrchestrator policyOrchestrator,
    IWorkflowStarter workflowStarter,
    IConfiguration configuration)
{
    private const string CommitteeWorkflowCode = "RECRUITMENT-SELECTION-COMMITTEE";

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
        if (session.WorkflowInstanceId is not null) return (false, "الجلسة قيد الاعتماد بالفعل.");
        if (!session.Members.Any()) return (false, "يجب إضافة عضو واحد على الأقل قبل التقديم.");

        var contextJson = JsonSerializer.Serialize(new
        {
            sessionId = session.Id,
            requisitionId = session.RequisitionId,
            memberCount = session.Members.Count(m => !m.Recused)
        });

        var mode = ResolveMode();
        if (mode == PolicyEnforcementMode.Off)
        {
            if (IsLabScale())
            {
                session.WorkflowInstanceId = await workflowStarter.StartByWorkflowCodeAsync(
                    CommitteeWorkflowCode,
                    "SelectionCommitteeSession",
                    session.Id,
                    session.TenantId,
                    actorUserId,
                    actorUserName,
                    contextJson,
                    PolicyDomains.CoreRecruitment,
                    ct);
            }

            await db.SaveChangesAsync(ct);
            return (true, null);
        }

        var policyRequest = PolicyDomainFactsBuilders.ForGeneric(
            PolicyDomains.CoreRecruitment,
            "SelectionCommitteeSession",
            "SubmitCommittee",
            session.Id,
            session.TenantId);
        policyRequest.ContextJson = contextJson;
        policyRequest.Mode = mode;
        policyRequest.ActorUserId = actorUserId;
        policyRequest.ActorUserName = actorUserName;

        var policyResult = await policyOrchestrator.EnforceAsync(policyRequest, ct);
        if (!policyResult.Allowed)
            return (false, policyResult.Decision.DenyReason ?? "مرفوض بسياسة اللجنة.");

        session.WorkflowInstanceId = policyResult.WorkflowInstanceId;
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
