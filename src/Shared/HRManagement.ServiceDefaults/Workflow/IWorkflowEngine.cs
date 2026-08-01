using HRManagement.SharedKernel.Workflow;

namespace HRManagement.ServiceDefaults.Workflow;

/// <summary>
/// Configurable workflow engine interface (WF-001).
/// Manages workflow instance lifecycle: start, approve step, reject.
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Starts a new workflow instance pinned to the current published definition version.
    /// </summary>
    WorkflowEngineResult Start(
        WorkflowDefinitionSnapshot definition,
        string entityType,
        Guid entityId,
        Guid? initiatedBy,
        string? initiatedByName,
        string? contextJson = null);

    /// <summary>
    /// Approves the current step of an in-progress workflow instance.
    /// </summary>
    WorkflowEngineResult ApproveStep(
        WorkflowInstanceState instance,
        WorkflowDefinitionSnapshot definition,
        Guid? actorId,
        string? actorName,
        string? comments = null,
        Dictionary<string, string>? context = null);

    /// <summary>
    /// Rejects the workflow at the current step.
    /// </summary>
    WorkflowEngineResult Reject(
        WorkflowInstanceState instance,
        Guid? actorId,
        string? actorName,
        string? comments = null);
}

/// <summary>Mutable state bag passed to the engine for evaluation.</summary>
public sealed class WorkflowInstanceState
{
    public Guid Id { get; set; }
    public int CurrentStepLevel { get; set; }
    public WorkflowStatus Status { get; set; }
    public List<WorkflowStepHistoryEntry> StepHistory { get; set; } = [];
    public string? ContextJson { get; set; }
}

public sealed record WorkflowEngineResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public WorkflowStatus NewStatus { get; init; }
    public int NewStepLevel { get; init; }
    public WorkflowStepHistoryEntry? HistoryEntry { get; init; }

    public static WorkflowEngineResult Ok(WorkflowStatus status, int stepLevel, WorkflowStepHistoryEntry? entry = null)
        => new() { Success = true, NewStatus = status, NewStepLevel = stepLevel, HistoryEntry = entry };

    public static WorkflowEngineResult Fail(string error)
        => new() { Success = false, Error = error };
}
