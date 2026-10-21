using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "شاغر داخلي")]
public class InternalVacancy : AuditableEntity
{
    [Required(ErrorMessage = "عنوان الشاغر مطلوب")]
    [Display(Name = "عنوان الشاغر")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    [Display(Name = "المؤهلات المطلوبة")]
    public string? RequiredQualifications { get; set; }

    [Display(Name = "تاريخ الإغلاق")]
    public DateOnly? ClosingDate { get; set; }

    [Display(Name = "مفتوح للترشّح")]
    public bool IsOpen { get; set; } = true;

    [Display(Name = "طلب الشاغر")]
    public Guid? RequisitionId { get; set; }

    public virtual RecruitmentRequisition? Requisition { get; set; }

    [Display(Name = "مشتق من طلب شاغر")]
    public bool IsDerivedFromRequisition { get; set; }

    public virtual OrgUnit? OrgUnit { get; set; }
    public virtual ICollection<InternalApplication> Applications { get; set; } = new List<InternalApplication>();
}
