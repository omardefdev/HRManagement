using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Talent;

/// <summary>HCM-005: an employee's skill proficiency record linked to the skills taxonomy.</summary>
public class SkillRecord : BaseEntity
{
    [Required]
    public Guid EmployeeId { get; set; }

    [Required, MaxLength(100)]
    public string SkillCode { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string SkillName { get; set; } = string.Empty;

    public SkillCategory Category { get; set; } = SkillCategory.Technical;

    /// <summary>Current proficiency level (1-5).</summary>
    public int ProficiencyLevel { get; set; }

    /// <summary>Required proficiency for the employee's current position.</summary>
    public int? RequiredLevel { get; set; }

    /// <summary>Date when this skill was last assessed or verified.</summary>
    public DateTime? AssessedDate { get; set; }

    /// <summary>Source of the assessment (self, manager, certification, test).</summary>
    [MaxLength(50)]
    public string? AssessmentSource { get; set; }

    /// <summary>Verifiable certificate/credential ID if externally validated.</summary>
    [MaxLength(200)]
    public string? CertificateId { get; set; }

    public DateTime? CertificateExpiry { get; set; }

    /// <summary>FK to the job catalog entry requiring this skill.</summary>
    public Guid? JobCatalogEntryId { get; set; }

    public int Gap => (RequiredLevel ?? 0) - ProficiencyLevel;

    public bool HasGap => RequiredLevel.HasValue && ProficiencyLevel < RequiredLevel.Value;

    public bool IsCertificateValid(DateTime asOf) =>
        CertificateId is not null && (CertificateExpiry is null || CertificateExpiry.Value >= asOf);
}

public enum SkillCategory
{
    Technical = 0,
    Managerial = 1,
    Behavioral = 2,
    Legal = 3,
    Language = 4,
    Specialized = 5
}
