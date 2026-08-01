namespace HRManagement.SharedKernel.Workflow;

/// <summary>
/// Immutable snapshot of a workflow step definition, serialized as part of the definition's StepsJson.
/// </summary>
public sealed record WorkflowStepSnapshot
{
    public int Level { get; init; }
    public string Name { get; init; } = string.Empty;
    public WorkflowStepType StepType { get; init; } = WorkflowStepType.Approval;
    public string RoleRequired { get; init; } = string.Empty;
    public WorkflowCondition? Condition { get; init; }
    public int? TimeoutHours { get; init; }
    public string? EscalationRole { get; init; }
}
