using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Domain.Entities.Workflow;

/// <summary>
/// A running or completed workflow instance, pinned to a specific definition version.
/// </summary>
public class WorkflowInstance : BaseEntity
{
    public Guid DefinitionId { get; set; }

    /// <summary>Version pinning — instance retains its original definition version even if definition is upgraded.</summary>
    public int DefinitionVersion { get; set; }

    [Required]
    [StringLength(100)]
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public int CurrentStepLevel { get; set; } = 1;

    public WorkflowStatus Status { get; set; } = WorkflowStatus.InProgress;

    /// <summary>JSON array of WorkflowStepHistoryEntry recording each action.</summary>
    public string StepHistoryJson { get; set; } = "[]";

    /// <summary>JSON context payload for condition evaluation.</summary>
    public string? ContextJson { get; set; }

    public Guid? InitiatedBy { get; set; }

    [StringLength(200)]
    public string? InitiatedByName { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    /// <summary>Optional link to legacy ApprovalRequest for dual-path bridge.</summary>
    public Guid? ApprovalRequestId { get; set; }
}
