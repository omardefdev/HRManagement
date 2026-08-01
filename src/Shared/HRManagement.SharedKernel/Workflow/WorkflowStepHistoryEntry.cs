namespace HRManagement.SharedKernel.Workflow;

/// <summary>
/// Immutable record of a step action, stored in WorkflowInstance.StepHistoryJson.
/// </summary>
public sealed record WorkflowStepHistoryEntry
{
    public int Level { get; init; }
    public string StepName { get; init; } = string.Empty;
    public WorkflowStepStatus Status { get; init; }
    public Guid? ActorId { get; init; }
    public string? ActorName { get; init; }
    public string? Comments { get; init; }
    public DateTime ActionAt { get; init; } = DateTime.UtcNow;
}
