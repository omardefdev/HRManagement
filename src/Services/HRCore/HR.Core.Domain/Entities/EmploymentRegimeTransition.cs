using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>SYR-003/SYR-07: append-only history of service regime transitions.</summary>
public class EmploymentRegimeTransition : AuditableEntity
{
    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid EmploymentRecordId { get; set; }

    [MaxLength(50)]
    public string? FromRegimeId { get; set; }

    [Required, MaxLength(50)]
    public string ToRegimeId { get; set; } = string.Empty;

    public DateTime EffectiveFrom { get; set; }

    [Required, MaxLength(200)]
    public string TransitionDecisionRef { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ConditionCode { get; set; }

    [MaxLength(1000)]
    public string? ConflictNotes { get; set; }

    public Guid? AppliedBy { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    public virtual Employee? Employee { get; set; }

    public virtual EmploymentRecord? EmploymentRecord { get; set; }
}
