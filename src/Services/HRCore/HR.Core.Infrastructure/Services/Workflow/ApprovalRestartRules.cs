using System.Text.Json;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>PLAN-25 (G-02): shared wording and audit for restarting an approval that was left without a workflow instance.</summary>
public static class ApprovalRestartRules
{
    public const string RestartAuditAction = "ApprovalWorkflowRestarted";
    public const string NotStuckMessage = "المعاملة ليست بانتظار الاعتماد دون مسار، فلا حاجة لإعادة بدء المسار.";
    public const string RestartButtonLabel = "إعادة بدء مسار الاعتماد";
    public const string StuckWarning =
        "هذه المعاملة بانتظار الاعتماد دون مسار اعتماد (بيانات سابقة لإصلاح PLAN-25). أعد بدء مسار الاعتماد.";

    public static void AddRestartAudit(HRCoreDbContext db, string entityType, Guid entityId, Guid workflowInstanceId, Guid? actorUserId) =>
        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = RestartAuditAction,
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new { workflowInstanceId })
        });
}
