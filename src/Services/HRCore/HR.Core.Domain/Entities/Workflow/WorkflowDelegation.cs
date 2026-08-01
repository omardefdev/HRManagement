using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities.Workflow;

/// <summary>
/// Delegation record allowing one user to act on behalf of another within workflow steps.
/// </summary>
public class WorkflowDelegation : BaseEntity
{
    public Guid PrincipalId { get; set; }

    [StringLength(200)]
    public string? PrincipalName { get; set; }

    public Guid DelegateId { get; set; }

    [StringLength(200)]
    public string? DelegateName { get; set; }

    [StringLength(100)]
    public string? WorkflowCode { get; set; }

    [StringLength(100)]
    public string? DomainEntityType { get; set; }

    public DateTime ActiveFrom { get; set; } = DateTime.UtcNow;

    public DateTime? ActiveUntil { get; set; }

    public bool IsRevoked { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}
