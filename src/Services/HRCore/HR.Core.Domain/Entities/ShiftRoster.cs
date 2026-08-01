using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "جدول المناوبات")]
public class ShiftRoster : AuditableEntity
{
    [Required(ErrorMessage = "عنوان الجدول مطلوب")]
    [Display(Name = "عنوان الجدول")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "بداية الفترة")]
    public DateOnly PeriodStart { get; set; }

    [Display(Name = "نهاية الفترة")]
    public DateOnly PeriodEnd { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public virtual OrgUnit? OrgUnit { get; set; }
    public virtual ICollection<ShiftAssignment> Assignments { get; set; } = new List<ShiftAssignment>();
}
