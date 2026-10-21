using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Localization;

namespace HRManagement.SharedKernel.MasterData;

/// <summary>
/// MDM-001: single-table national reference data entry.
/// Code is unique per <see cref="CatalogKind"/> and tenant.
/// </summary>
public class ReferenceCatalogEntry : AuditableEntity, ILocalizedName
{
    [Required]
    public CatalogKind CatalogKind { get; set; }

    [Required]
    [MaxLength(50)]
    [Display(Name = "الرمز")]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(300)]
    [Display(Name = "الاسم بالعربية")]
    public string NameAr { get; set; } = string.Empty;

    [MaxLength(300)]
    [Display(Name = "الاسم بالإنجليزية")]
    public string? NameEn { get; set; }

    [MaxLength(300)]
    [Display(Name = "الاسم بالفرنسية")]
    public string? NameFr { get; set; }

    [Display(Name = "صالح من")]
    public DateTime? ValidFrom { get; set; }

    [Display(Name = "صالح حتى")]
    public DateTime? ValidTo { get; set; }

    [MaxLength(50)]
    [Display(Name = "استُبدل برمز")]
    public string? ReplacedByCode { get; set; }

    [MaxLength(200)]
    [Display(Name = "الجهة المصدرية")]
    public string? SourceAuthority { get; set; }

    [MaxLength(100)]
    [Display(Name = "دور الإشراف")]
    public string? StewardRole { get; set; }

    [Display(Name = "فعّال")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "الإصدار")]
    public int Version { get; set; } = 1;
}
