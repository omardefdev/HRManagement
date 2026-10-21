using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-003: defines a public service legal regime (Law 50/2004, Law 53/2021, or special regime).</summary>
public class LegalRegime
{
    [Key]
    [Required, MaxLength(50)]
    public string LegalRegimeId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string RegimeName { get; set; } = string.Empty;

    public RegimeType Type { get; set; }

    /// <summary>Primary legislation reference (e.g. "law50_2004", "law53_2021").</summary>
    [Required, MaxLength(100)]
    public string LegislationRef { get; set; } = string.Empty;

    /// <summary>SYR-001: resolved canonical legal source after alias lookup (nullable for special regimes).</summary>
    [MaxLength(100)]
    public string? ResolvedLegalSourceId { get; set; }

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual LegislationSource? ResolvedLegalSource { get; set; }

    public bool IsEffectiveOn(DateTime date) =>
        IsActive && date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo.Value);

    public bool IsLaw50 => Type == RegimeType.Law50;
    public bool IsLaw53 => Type == RegimeType.Law53;
    public bool IsSpecial => Type == RegimeType.Special;
}

public enum RegimeType
{
    [Display(Name = "قانون 50")]
    Law50 = 0,
    [Display(Name = "قانون 53")]
    Law53 = 1,
    [Display(Name = "نظام خاص")]
    Special = 2
}

/// <summary>Employment types under Syrian public service law.</summary>
public enum EmploymentType
{
    [Display(Name = "دائم")]
    Permanent = 0,
    [Display(Name = "مؤقت")]
    Temporary = 1,
    [Display(Name = "يومي")]
    Daily = 2,
    [Display(Name = "موسمي")]
    Seasonal = 3,
    [Display(Name = "دوام جزئي")]
    PartTime = 4,
    [Display(Name = "إنتاج")]
    Production = 5,
    [Display(Name = "تعاقدي")]
    Contractual = 6
}
