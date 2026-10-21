using HRManagement.SharedKernel.Workflow;

namespace HRManagement.ServiceDefaults.Workflow;

/// <summary>WF-001 / PLAN-03: API guards against illegal stage transitions.</summary>
public static class WorkflowStageGuard
{
    public static string? ValidateApprove(
        WorkflowInstanceState state,
        WorkflowDefinitionSnapshot definition,
        Guid? actorId,
        Guid? initiatedBy,
        int? targetStepLevel)
    {
        if (state.Status != WorkflowStatus.InProgress)
            return "Workflow instance is not in progress.";

        if (targetStepLevel.HasValue && targetStepLevel.Value != state.CurrentStepLevel)
            return "Cannot skip workflow stages: target step must match the current step.";

        if (definition.Steps.All(s => s.Level != state.CurrentStepLevel))
            return $"Step at level {state.CurrentStepLevel} not found in definition.";

        if (actorId.HasValue && initiatedBy.HasValue
            && actorId.Value == initiatedBy.Value
            && state.CurrentStepLevel > 1)
        {
            return "Workflow initiator cannot approve subsequent approval stages.";
        }

        return null;
    }
}
