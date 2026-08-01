using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;

namespace HR.Health.Domain.Entities;

[Display(Name = "الملف الطبي")]
public class MedicalRecord : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "التاريخ الطبي")]
    [AuditRedact]
    public string? MedicalHistory { get; set; }

    [Display(Name = "الحالات الحالية")]
    [AuditRedact]
    public string? CurrentConditions { get; set; }

    [Display(Name = "الحساسيات")]
    [AuditRedact]
    public string? Allergies { get; set; }

    [Display(Name = "الإعاقات")]
    [AuditRedact]
    public string? Disabilities { get; set; }

    [Display(Name = "تاريخ آخر فحص")]
    public DateOnly? LastExamDate { get; set; }

    /// <summary>FUN-004: consent purpose/window.</summary>
    public DateTime? ConsentGivenAt { get; set; }

    [MaxLength(256)]
    public string? ConsentPurpose { get; set; }

    public DateTime? ConsentExpiresAt { get; set; }
    public DateTime? ConsentWithdrawnAt { get; set; }

    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public ICollection<MedicalCommitteeReport> CommitteeReports { get; set; } = new List<MedicalCommitteeReport>();
}
