using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities.Workflow;

/// <summary>
/// Configurable workflow definition with versioning and effective-date control.
/// Steps are stored as JSON (WorkflowStepSnapshot[]) for flexibility without schema changes.
/// </summary>
public class WorkflowDefinition : BaseEntity
{
    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public DateTime? ValidFrom { get; set; }

    public DateTime? ValidTo { get; set; }

    public bool IsPublished { get; set; }

    [Required]
    [StringLength(100)]
    public string DomainEntityType { get; set; } = string.Empty;

    /// <summary>JSON array of WorkflowStepSnapshot.</summary>
    public string StepsJson { get; set; } = "[]";

    /// <summary>JSON array of role names that act as guardrails (domain invariant constraints).</summary>
    public string GuardrailRolesJson { get; set; } = "[]";

    [StringLength(1000)]
    public string? Description { get; set; }
}
