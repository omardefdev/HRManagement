using HRManagement.SharedKernel.Workflow;

namespace HRManagement.ServiceDefaults.Workflow;

/// <summary>
/// Default implementation of the configurable workflow engine (WF-001).
/// Supports sequential step execution with condition gating, version pinning,
/// and guardrail role validation.
/// </summary>
public sealed class WorkflowEngine : IWorkflowEngine
{
    public WorkflowEngineResult Start(
        WorkflowDefinitionSnapshot definition,
        string entityType,
        Guid entityId,
        Guid? initiatedBy,
        string? initiatedByName,
        string? contextJson = null)
    {
        if (definition.Steps.Count == 0)
            return WorkflowEngineResult.Fail("Workflow definition has no steps.");

        var firstStep = definition.Steps.OrderBy(s => s.Level).First();

        var context = WorkflowCondition.ParseContext(contextJson);
        if (firstStep.Condition is not null && !firstStep.Condition.Evaluate(context))
        {
            var nextApplicable = FindNextApplicableStep(definition.Steps, firstStep.Level, context);
            if (nextApplicable is null)
            {
                return WorkflowEngineResult.Ok(WorkflowStatus.Completed, firstStep.Level);
            }
            return WorkflowEngineResult.Ok(WorkflowStatus.InProgress, nextApplicable.Level);
        }

        return WorkflowEngineResult.Ok(WorkflowStatus.InProgress, firstStep.Level);
    }

    public WorkflowEngineResult ApproveStep(
        WorkflowInstanceState instance,
        WorkflowDefinitionSnapshot definition,
        Guid? actorId,
        string? actorName,
        string? comments = null,
        Dictionary<string, string>? context = null)
    {
        if (instance.Status != WorkflowStatus.InProgress)
            return WorkflowEngineResult.Fail("Workflow instance is not in progress.");

        var currentStep = definition.Steps.FirstOrDefault(s => s.Level == instance.CurrentStepLevel);
        if (currentStep is null)
            return WorkflowEngineResult.Fail($"Step at level {instance.CurrentStepLevel} not found in definition.");

        if (actorId.HasValue && instance.StepHistory.Any(h =>
            h.Level == instance.CurrentStepLevel &&
            h.Status == WorkflowStepStatus.Approved &&
            h.ActorId == actorId))
        {
            return WorkflowEngineResult.Fail("Same actor cannot approve the same step twice.");
        }

        var historyEntry = new WorkflowStepHistoryEntry
        {
            Level = instance.CurrentStepLevel,
            StepName = currentStep.Name,
            Status = WorkflowStepStatus.Approved,
            ActorId = actorId,
            ActorName = actorName,
            Comments = comments,
            ActionAt = DateTime.UtcNow
        };

        var evalContext = context ?? WorkflowCondition.ParseContext(instance.ContextJson);
        var nextStep = FindNextApplicableStep(definition.Steps, instance.CurrentStepLevel, evalContext);

        if (nextStep is null)
        {
            return WorkflowEngineResult.Ok(WorkflowStatus.Completed, instance.CurrentStepLevel, historyEntry);
        }

        return WorkflowEngineResult.Ok(WorkflowStatus.InProgress, nextStep.Level, historyEntry);
    }

    public WorkflowEngineResult Reject(
        WorkflowInstanceState instance,
        Guid? actorId,
        string? actorName,
        string? comments = null)
    {
        if (instance.Status != WorkflowStatus.InProgress)
            return WorkflowEngineResult.Fail("Workflow instance is not in progress.");

        var historyEntry = new WorkflowStepHistoryEntry
        {
            Level = instance.CurrentStepLevel,
            StepName = $"Step {instance.CurrentStepLevel}",
            Status = WorkflowStepStatus.Rejected,
            ActorId = actorId,
            ActorName = actorName,
            Comments = comments,
            ActionAt = DateTime.UtcNow
        };

        return WorkflowEngineResult.Ok(WorkflowStatus.Rejected, instance.CurrentStepLevel, historyEntry);
    }

    private static WorkflowStepSnapshot? FindNextApplicableStep(
        IReadOnlyList<WorkflowStepSnapshot> steps,
        int currentLevel,
        Dictionary<string, string>? context)
    {
        var candidates = steps
            .Where(s => s.Level > currentLevel)
            .OrderBy(s => s.Level);

        foreach (var step in candidates)
        {
            if (step.Condition is null || step.Condition.Evaluate(context))
                return step;
        }

        return null;
    }
}
