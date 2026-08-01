using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Entities;

public class EmploymentRecord : AuditableEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "المسمى الوظيفي")]
    [Required(ErrorMessage = "المسمى الوظيفي مطلوب")]
    [StringLength(200)]
    public string PositionTitle { get; set; } = string.Empty;

    [Display(Name = "كود الوظيفة")]
    [StringLength(50)]
    public string? PositionCode { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "تاريخ البداية")]
    [Required(ErrorMessage = "تاريخ البداية مطلوب")]
    [DataType(DataType.Date)]
    public DateTime StartDate { get; set; }

    [Display(Name = "تاريخ النهاية")]
    [DataType(DataType.Date)]
    public DateTime? EndDate { get; set; }

    [Display(Name = "نوع التوظيف")]
    public EmploymentType EmploymentType { get; set; }

    [Display(Name = "الحالة")]
    [StringLength(20)]
    public string Status { get; set; } = "active";

    [Display(Name = "المعتمد")]
    public Guid? ApprovedBy { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>MDM-001: optional FK to ReferenceCatalogEntry (CatalogKind = JobClassification).</summary>
    [Display(Name = "رمز التصنيف الوظيفي")]
    [StringLength(50)]
    public string? JobClassificationCode { get; set; }

    public virtual Employee? Employee { get; set; }
    public virtual OrgUnit? OrgUnit { get; set; }
}
