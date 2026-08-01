using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class RecruitmentApplication : AuditableEntity
{
    [Required]
    [Display(Name = "اسم المتقدم")]
    public string ApplicantFullName { get; set; } = string.Empty;

    [Display(Name = "الرقم الوطني")]
    public string? NationalId { get; set; }

    [Required]
    [Display(Name = "الوظيفة المتقدم لها")]
    public string AppliedPosition { get; set; } = string.Empty;

    [Display(Name = "تاريخ التقديم")]
    public DateTime AppliedAt { get; set; }

    [Display(Name = "الحالة")]
    public RecruitmentApplicationStatus Status { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "موظف بعد التعيين")]
    public Guid? HiredEmployeeId { get; set; }

    [Display(Name = "تاريخ المراجعة")]
    public DateTime? ReviewedAt { get; set; }

    public virtual Employee? HiredEmployee { get; set; }
}
