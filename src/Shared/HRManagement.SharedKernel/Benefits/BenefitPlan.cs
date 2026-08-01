using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

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

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsEffectiveOn(DateTime date) =>
        IsActive && date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo.Value);
}

public enum BenefitPlanType
{
    Health = 0,
    Pension = 1,
    SocialInsurance = 2,
    Allowance = 3,
    Other = 99
}
