using HR.Core.Domain.Entities.Workflow;
using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>PLAN-44 (G-WF-12): what a decision that lost a race is told, and how it is recognised.</summary>
public static class WorkflowConcurrency
{
    public const string ConflictMessage = "تغيّرت حالة الطلب أثناء الإجراء؛ أعد تحميل الصفحة وحاول مجدداً";

    /// <summary>
    /// The page the actor decided on showed <paramref name="targetStepLevel"/>, and the instance has moved past it or finished. Without
    /// this, a second click that reads after the first committed would act on the next step, or see a refusal about a step it never saw.
    /// A target ahead of the current step stays the stage guard's refusal.
    /// </summary>
    public static bool IsStale(WorkflowInstance instance, int? targetStepLevel) =>
        targetStepLevel is int target
        && (instance.Status != WorkflowStatus.InProgress || target < instance.CurrentStepLevel);
}

/// <summary>
/// PLAN-44: a handler cannot apply the outcome for a business reason. The decision's transaction rolls back and the actor sees the message.
/// </summary>
public sealed class WorkflowOutcomeRefusedException(string message) : Exception(message);

/// <summary>PLAN-44: this entity has a running instance already. Nothing of this start was saved.</summary>
public sealed class WorkflowInstanceRunningException(string entityType, Guid entityId) : Exception(RunningMessage)
{
    public const string RunningMessage = "يوجد مسار جارٍ لهذا الطلب";

    public string EntityType { get; } = entityType;

    public Guid EntityId { get; } = entityId;
}
