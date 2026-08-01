using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-003: defines a public service legal regime (Law 50/2004, Law 53/2021, or special regime).</summary>
public class LegalRegime : BaseEntity
{
    [Required, MaxLength(50)]
    public string LegalRegimeId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string RegimeName { get; set; } = string.Empty;

    public RegimeType Type { get; set; }

    /// <summary>Primary legislation reference (e.g. "law50_2004", "law53_2021").</summary>
    [Required, MaxLength(100)]
    public string LegislationRef { get; set; } = string.Empty;

    /// <summary>Employment types allowed under this regime.</summary>
    [MaxLength(500)]
    public string? AllowedEmploymentTypes { get; set; }

    /// <summary>Grade/rank structure identifier for this regime.</summary>
    [MaxLength(100)]
    public string? GradeStructureId { get; set; }

    /// <summary>Whether promotion rules from this regime apply.</summary>
    public bool HasPromotionRules { get; set; }

    /// <summary>Whether this regime covers pension/social insurance.</summary>
    public bool CoversPension { get; set; }

    /// <summary>Effective start date of this regime.</summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>Effective end date (null = still in force).</summary>
    public DateTime? EffectiveTo { get; set; }

    /// <summary>Decision/decree authorizing transition to this regime.</summary>
    [MaxLength(200)]
    public string? TransitionDecisionRef { get; set; }

    /// <summary>Whether employees can be assigned to this regime.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Special notes on conflicting entitlements (Article 159 cases).</summary>
    [MaxLength(1000)]
    public string? ConflictNotes { get; set; }

    public bool IsEffectiveOn(DateTime date) =>
        IsActive && date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo.Value);

    public bool IsLaw50 => Type == RegimeType.Law50;
    public bool IsLaw53 => Type == RegimeType.Law53;
    public bool IsSpecial => Type == RegimeType.Special;
}

public enum RegimeType
{
    Law50 = 0,
    Law53 = 1,
    Special = 2
}

/// <summary>Employment types under Syrian public service law.</summary>
public enum EmploymentType
{
    Permanent = 0,
    Temporary = 1,
    Daily = 2,
    Seasonal = 3,
    PartTime = 4,
    Production = 5,
    Contractual = 6
}
