using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>ORG-001: type of structural change that produced this version.</summary>
public enum OrgOperationType
{
    Create = 0,
    Rename = 1,
    Move = 2,
    Merge = 3,
    Split = 4,
    Close = 5
}

/// <summary>
/// ORG-001: dated version of an org unit. Every structural change creates a new
/// version with ValidFrom/ValidTo; history is never overwritten. As-of queries
/// resolve the active version for any point in time.
/// </summary>
public class OrgUnitVersion : AuditableEntity
{
    /// <summary>FK to the stable OrgUnit identity.</summary>
    public Guid OrgUnitId { get; set; }

    /// <summary>Start of validity (inclusive).</summary>
    [Display(Name = "صالح من")]
    public DateTime ValidFrom { get; set; }

    /// <summary>End of validity (exclusive). Null = current/open-ended.</summary>
    [Display(Name = "صالح حتى")]
    public DateTime? ValidTo { get; set; }

    [Display(Name = "الاسم بالعربي")]
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "الاسم بالإنكليزي")]
    [MaxLength(200)]
    public string? NameEn { get; set; }

    /// <summary>Parent org unit at this point in time.</summary>
    [Display(Name = "الوحدة الأم")]
    public Guid? ParentId { get; set; }

    /// <summary>Type of change that produced this version.</summary>
    [Display(Name = "نوع العملية")]
    public OrgOperationType OperationType { get; set; } = OrgOperationType.Create;

    /// <summary>Actor who approved the structural change.</summary>
    [MaxLength(200)]
    public string? ApprovedBy { get; set; }

    /// <summary>Decision/decree reference number.</summary>
    [MaxLength(100)]
    public string? DecisionReference { get; set; }

    public virtual OrgUnit? OrgUnit { get; set; }
}
