using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "ترشّح داخلي")]
public class InternalApplication : AuditableEntity
{
    [Display(Name = "الشاغر الداخلي")]
    public Guid InternalVacancyId { get; set; }

    [Display(Name = "الموظف المتقدّم")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "تاريخ الترشّح")]
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "الحالة")]
    public InternalApplicationStatus Status { get; set; } = InternalApplicationStatus.Submitted;

    [Display(Name = "مسوّغات الترشّح")]
    public string? Motivation { get; set; }

    [Display(Name = "طلب الشاغر")]
    public Guid? RequisitionId { get; set; }

    public virtual RecruitmentRequisition? Requisition { get; set; }

    [Display(Name = "طلب التوظيف المرتبط")]
    public Guid? PromotedRecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? PromotedRecruitmentApplication { get; set; }

    public virtual InternalVacancy? InternalVacancy { get; set; }
    public virtual Employee? Employee { get; set; }
}
