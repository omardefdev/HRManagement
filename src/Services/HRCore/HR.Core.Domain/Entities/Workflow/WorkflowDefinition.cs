using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;

namespace HR.Core.Domain.Entities.Workflow;

/// <summary>
/// Configurable workflow definition with versioning and effective-date control.
/// Steps are stored as JSON (WorkflowStepSnapshot[]) for flexibility without schema changes.
/// </summary>
public class WorkflowDefinition : BaseEntity, INationalShared
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

    /// <summary>Kept in step with <see cref="Status"/> for the code that starts and lists definitions.</summary>
    public bool IsPublished { get; set; }

    /// <summary>PLAN-43 (G-WF-04): changes only through <c>WorkflowDefinitionLifecycleService</c> and the seeder.</summary>
    public WorkflowDefinitionStatus Status { get; set; } = WorkflowDefinitionStatus.Draft;

    public Guid? CreatedBy { get; set; }

    /// <summary>JSON array of everyone who changed the draft: the second person must not be someone who wrote what is published.</summary>
    public string? EditorUserIdsJson { get; set; }

    public Guid? SubmittedBy { get; set; }

    public DateTime? SubmittedAt { get; set; }

    /// <summary>Null for a version the seeder published.</summary>
    public Guid? PublishedBy { get; set; }

    public DateTime? PublishedAt { get; set; }

    public Guid? RetiredBy { get; set; }

    public DateTime? RetiredAt { get; set; }

    [StringLength(500)]
    public string? ChangeReason { get; set; }

    /// <summary>PLAN-43 (G-WF-06): one person may not approve two steps of the same instance.</summary>
    public bool RequireDistinctApprovers { get; set; }

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
