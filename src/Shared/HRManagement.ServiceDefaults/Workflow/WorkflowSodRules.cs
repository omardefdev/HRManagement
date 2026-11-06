using HRManagement.SharedKernel.Workflow;

namespace HRManagement.ServiceDefaults.Workflow;

/// <summary>What the separation-of-duties rules read from an instance and its pinned definition.</summary>
public sealed record WorkflowSodFacts(
    Guid? RequestedByUserId,
    IReadOnlyCollection<Guid> ExcludedApproverUserIds,
    Guid? SubjectEmployeeId,
    bool RequireDistinctApprovers,
    int CurrentStepLevel,
    IReadOnlyList<WorkflowStepHistoryEntry> History);

/// <summary>
/// PLAN-43 (G-WF-06, D-WF-8): the three rules of every workflow type, as pure functions, so the server's evaluator and the inbox panel
/// judge alike. <c>Workflow:AdminBypass</c> never reaches them. Instances started inside HR.Core have no requester, so the recruitment
/// and establishment rules stay in their gates.
/// </summary>
public static class WorkflowSodRules
{
    public const string ExcludedMessage = "لا يجوز لطالب الإجراء أو المستبعَد منه اعتماده أو رفضه.";
    public const string SubjectMessage = "لا يجوز للموظف المعني اعتماد إجراء يخصه.";
    public const string DistinctApproverMessage = "هذا المسار يتطلب معتمداً مختلفاً لكل خطوة.";

    /// <summary>The requester and the excluded users: every step, approving and rejecting.</summary>
    public static string? Excluded(WorkflowSodFacts facts, Guid? actorUserId) =>
        actorUserId is Guid actor && (actor == facts.RequestedByUserId || facts.ExcludedApproverUserIds.Contains(actor))
            ? ExcludedMessage
            : null;

    /// <summary>The employee the request is about: every step, approving and rejecting.</summary>
    public static string? Subject(WorkflowSodFacts facts, Guid? actorEmployeeId) =>
        actorEmployeeId is Guid employee && employee == facts.SubjectEmployeeId ? SubjectMessage : null;

    /// <summary>Approving only: an actor who approved another level of this instance, when the definition requires it.</summary>
    public static string? DistinctApprover(WorkflowSodFacts facts, Guid? actorUserId) =>
        facts.RequireDistinctApprovers
        && actorUserId is Guid actor
        && facts.History.Any(h => h.Status == WorkflowStepStatus.Approved && h.ActorId == actor && h.Level != facts.CurrentStepLevel)
            ? DistinctApproverMessage
            : null;

    /// <summary>The rules for one action, in the order of their messages.</summary>
    public static string? Evaluate(WorkflowSodFacts facts, Guid? actorUserId, Guid? actorEmployeeId, bool approving) =>
        Excluded(facts, actorUserId)
        ?? Subject(facts, actorEmployeeId)
        ?? (approving ? DistinctApprover(facts, actorUserId) : null);
}
