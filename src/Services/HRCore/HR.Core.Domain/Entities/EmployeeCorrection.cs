using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Entities;

/// <summary>DATA-002: demographic correction with before/after, reason, and actor (not EntityAuditLog alone).</summary>
public class EmployeeCorrection : AuditableEntity
{
    [Required]
    public Guid EmployeeId { get; set; }

    /// <summary>JSON: { "before": { ... }, "after": { ... } }.</summary>
    [Required]
    public string ChangesJson { get; set; } = "{}";

    [Required(ErrorMessage = "سبب التصحيح مطلوب")]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;

    public Guid RequestedBy { get; set; }

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public Guid? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>True when ChangesJson touches NationalId, BirthDate, or HireDate.</summary>
    public bool RequiresLegalApproval { get; set; }

    public virtual Employee? Employee { get; set; }
}
