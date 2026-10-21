using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Benefits;

/// <summary>HCM-004: tracks an employee's enrollment in a pension/benefit plan with service credit and contributions.</summary>
public class PensionEnrollment : BaseEntity
{
    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid BenefitPlanId { get; set; }

    public DateTime EnrollmentDate { get; set; }

    public DateTime? TerminationDate { get; set; }

    public int ServiceCreditMonths { get; set; }

    public decimal ContributionBalance { get; set; }

    /// <summary>JSON-serialized dependent/beneficiary information.</summary>
    public string? BeneficiaryInfo { get; set; }

    public PensionEnrollmentStatus Status { get; set; } = PensionEnrollmentStatus.Active;

    /// <summary>SYR-003: legal regime identifier (law50/law53/special).</summary>
    [MaxLength(50)]
    public string? LegalRegimeId { get; set; }

    public BenefitPlan? BenefitPlan { get; set; }

    public bool IsActive => Status == PensionEnrollmentStatus.Active && TerminationDate is null;
}

public enum PensionEnrollmentStatus
{
    [Display(Name = "نشط")]
    Active = 0,
    [Display(Name = "موقوف")]
    Suspended = 1,
    [Display(Name = "منتهي")]
    Terminated = 2,
    [Display(Name = "متقاعد")]
    Retired = 3
}
