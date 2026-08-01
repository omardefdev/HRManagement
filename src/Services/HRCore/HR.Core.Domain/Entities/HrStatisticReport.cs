using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class HrStatisticReport : AuditableEntity
{
    [Required]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "بداية الفترة")]
    public DateOnly PeriodStart { get; set; }

    [Display(Name = "نهاية الفترة")]
    public DateOnly PeriodEnd { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "ملخص المؤشرات")]
    public string? MetricsSummary { get; set; }

    public virtual OrgUnit? OrgUnit { get; set; }
}
