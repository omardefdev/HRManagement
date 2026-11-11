using System.Text.Json.Serialization;

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

    /// <summary>
    /// PLAN-50 (§6): the principal for whom <see cref="ActorId"/> acted through a delegation. Optional: older history reads as before, and a
    /// direct action writes none of the three fields.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? OnBehalfOfId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OnBehalfOfName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DelegationId { get; init; }

    /// <summary>PLAN-50: the action counts for the actor and, when he acted on behalf, for his principal (distinct-approver rules).</summary>
    public bool WasActedBy(Guid userId) => ActorId == userId || OnBehalfOfId == userId;
}
