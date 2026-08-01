using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Dev.Analytics.Domain.Entities;

[Display(Name = "مؤشر الأداء")]
public class KPI : AuditableEntity
{
    [Display(Name = "الرمز")]
    [Required(ErrorMessage = "الرمز مطلوب")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "الاسم")]
    [Required(ErrorMessage = "الاسم مطلوب")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "التعريف")]
    public string? Definition { get; set; }

    [Display(Name = "القيمة المستهدفة")]
    public decimal? TargetValue { get; set; }

    [Display(Name = "القيمة الحالية")]
    public decimal? CurrentValue { get; set; }

    [Display(Name = "تاريخ القياس")]
    public DateTime? MeasuredAt { get; set; }
}
