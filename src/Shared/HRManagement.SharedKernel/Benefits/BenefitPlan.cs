using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.SharedKernel.Benefits;

/// <summary>HCM-004: a configured benefit/insurance/pension plan with eligibility, rates, and effective dates.</summary>
public class BenefitPlan : BaseEntity
{
    [Required, MaxLength(50)]
    public string PlanCode { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string PlanName { get; set; } = string.Empty;

    public BenefitPlanType PlanType { get; set; }

    [MaxLength(200)]
    public string? ProviderName { get; set; }

    public decimal EmployeeContributionRate { get; set; }

    public decimal EmployerContributionRate { get; set; }

    public decimal? CeilingAmount { get; set; }

    public int EligibilityMinServiceMonths { get; set; }

    /// <summary>SYR-001: law/decree reference for this plan's rules.</summary>
    [MaxLength(100)]
    public string? LegalSourceId { get; set; }

    /// <summary>SYR-003: optional link to the service regime this plan applies under.</summary>
    [MaxLength(50)]
    public string? LegalRegimeId { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsEffectiveOn(DateTime date) =>
        IsActive && date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo.Value);

    public virtual LegislationSource? LegalSource { get; set; }

    public virtual LegalRegime? LegalRegime { get; set; }
}

public enum BenefitPlanType
{
    [Display(Name = "صحية")]
    Health = 0,
    [Display(Name = "تقاعد")]
    Pension = 1,
    [Display(Name = "تأمينات اجتماعية")]
    SocialInsurance = 2,
    [Display(Name = "علاوة")]
    Allowance = 3,
    [Display(Name = "أخرى")]
    Other = 99
}
