using System.Text.Json;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>
/// PLAN-49 (G-POL-09): a policy may refuse an approval, or start the very workflow the service asked for. It never chooses
/// another one. The service computes the code from the state of its entity (an exception offer asks for
/// RECRUITMENT-OFFER-OVERRIDE-APPROVAL, an ordinary one for RECRUITMENT-OFFER-APPROVAL); a rule that fires a weaker code for
/// the same action would otherwise swap the stricter approval for it, and the outcome handlers only check that the instance
/// is the one linked to the entity.
/// <para>
/// When the engine answers with an instance of another code, that instance is cancelled here the way a repair cancels one
/// (status, a history entry, an audit row), in the caller's unit of work. <see cref="ApprovalStartCoordinator"/> opens or joins
/// a transaction around the start, so there the cancel, the refusal and the policy ledger row commit together. Starting an
/// instance writes the instance row and nothing else (no message, no notification), so nothing announces the one that is
/// cancelled; a cancelled instance is not in the work inbox (it lists InProgress) and is never reused (the coordinator reuses
/// InProgress only).
/// </para>
/// <para>
/// The appeal review (RecruitmentAppealService) asks the policy outside the coordinator and opens no transaction of its own: its
/// instance, its ledger row and this cancel are separate saves there. A rule the validator accepts can name only the review's
/// own workflow for FileAppeal, so that branch is reached by unvalidated data alone; making the appeal's start one unit of
/// work goes with its ordering (it stages the reviewer before it asks), recorded for the wave that turns the domain to Enforce.
/// </para>
/// </summary>
public static class PolicyWorkflowCodeGuard
{
    public const string RefusedMessage = "السياسة اختارت مسار اعتماد غير مسموح لهذا الإجراء";
    public const string CancelStepName = "إلغاء (PLAN-49)";
    public const string CancelAuditAction = "PolicyWorkflowCodeRefused";

    /// <returns>
    /// Null when the instance runs <paramref name="requestedCode"/>. Otherwise <see cref="RefusedMessage"/>: the instance is
    /// cancelled and saved, or, when no such instance can be read, there is nothing to cancel and nothing to accept.
    /// </returns>
    public static async Task<string?> RefuseOtherWorkflowAsync(
        HRCoreDbContext db,
        Guid policyInstanceId,
        string requestedCode,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct)
    {
        var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == policyInstanceId, ct);
        if (instance is null)
            return RefusedMessage;

        // PLAN-48: the national read path (the session tenant's or the national definition).
        var startedCode = await db.WorkflowDefinitions
            .OwnOrNational(db)
            .AsNoTracking()
            .Where(d => d.Id == instance.DefinitionId)
            .Select(d => d.Code)
            .FirstOrDefaultAsync(ct);
        if (string.Equals(startedCode, requestedCode, StringComparison.OrdinalIgnoreCase))
            return null;

        var now = DateTime.UtcNow;
        var reason = $"{RefusedMessage}: {startedCode ?? "?"} بدل {requestedCode}";
        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];
        history.Add(new WorkflowStepHistoryEntry
        {
            Level = instance.CurrentStepLevel,
            StepName = CancelStepName,
            Status = WorkflowStepStatus.Rejected,
            ActorId = actorUserId,
            ActorName = actorUserName,
            Comments = reason,
            ActionAt = now
        });
        instance.StepHistoryJson = JsonSerializer.Serialize(history);
        instance.Status = WorkflowStatus.Cancelled;
        instance.CompletedAt = now;
        // The audit log is a global table (TenantEntityClassification): nothing stamps its tenant, and a tenant's own audit
        // view filters on it. The row belongs to the tenant of the instance it explains.
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = instance.TenantId,
            EntityType = instance.EntityType,
            EntityId = instance.EntityId,
            Action = CancelAuditAction,
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new { workflowInstanceId = instance.Id, startedCode, requestedCode })
        });
        await db.SaveChangesAsync(ct);
        return RefusedMessage;
    }
}
