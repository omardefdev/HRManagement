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

    /// <summary>PLAN-43 (G-WF-05): the human behind a service start, derived on the server. Null for instances started inside HR.Core.</summary>
    public Guid? RequestedByUserId { get; set; }

    [StringLength(200)]
    public string? RequestedByName { get; set; }

    /// <summary>The service account that started the instance.</summary>
    [StringLength(100)]
    public string? SourceClientId { get; set; }

    [StringLength(200)]
    public string? IdempotencyKey { get; set; }

    public Guid? ScopeOrgUnitId { get; set; }

    /// <summary>The employee the request is about: never an approver of it.</summary>
    public Guid? SubjectEmployeeId { get; set; }

    /// <summary>JSON array of user ids barred from every step, besides the requester.</summary>
    public string? ExcludedApproverUserIdsJson { get; set; }

    [StringLength(300)]
    public string? DisplayTitle { get; set; }

    [StringLength(500)]
    public string? DetailsUrl { get; set; }
}
