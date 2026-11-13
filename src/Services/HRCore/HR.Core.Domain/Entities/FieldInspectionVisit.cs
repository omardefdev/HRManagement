using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class FieldInspectionVisit : AuditableEntity
{
    [Required]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الموقع")]
    public string? Location { get; set; }

    [Display(Name = "تاريخ الزيارة")]
    public DateOnly VisitDate { get; set; }

    [Display(Name = "قائد التفتيش")]
    public Guid? LeadInspectorId { get; set; }

    [Display(Name = "الملخص")]
    public string? Summary { get; set; }

    [Display(Name = "الحالة")]
    [StringLength(32)]
    public string Status { get; set; } = "planned";

    /// <summary>PLAN-52 (D-52-2): the visit's org unit: the restricted creator's scope root, else the central user's choice; empty =
    /// central (D-52-1). Old visits stay empty: <see cref="LeadInspectorId"/> is the inspector, not the subject.</summary>
    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    public virtual OrgUnit? OrgUnit { get; set; }
}
