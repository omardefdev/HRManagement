using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "مرحلة المسار الوظيفي")]
public class CareerPathStep : AuditableEntity
{
    [Display(Name = "المسار الوظيفي")]
    public Guid CareerPathId { get; set; }

    [Display(Name = "الترتيب")]
    public int Order { get; set; }

    [Required(ErrorMessage = "المسمى الوظيفي مطلوب")]
    [Display(Name = "المسمى الوظيفي")]
    [StringLength(200)]
    public string PositionTitle { get; set; } = string.Empty;

    [Display(Name = "الحد الأدنى لسنوات الخبرة")]
    [Range(0, 50)]
    public int MinYears { get; set; }

    [Display(Name = "المؤهل/المتطلبات")]
    public string? Requirements { get; set; }

    /// <summary>MDM-001: optional FK to ReferenceCatalogEntry (CatalogKind = JobClassification).</summary>
    [Display(Name = "رمز التصنيف الوظيفي")]
    [StringLength(50)]
    public string? JobClassificationCode { get; set; }

    public virtual CareerPath? CareerPath { get; set; }
}
