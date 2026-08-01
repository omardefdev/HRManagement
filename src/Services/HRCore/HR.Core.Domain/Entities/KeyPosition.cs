using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "المنصب الحرج")]
public class KeyPosition : AuditableEntity
{
    [Required(ErrorMessage = "عنوان المنصب مطلوب")]
    [Display(Name = "عنوان المنصب")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "شاغل المنصب الحالي")]
    public Guid? CurrentHolderEmployeeId { get; set; }

    [Display(Name = "درجة الأهمية (1-5)")]
    [Range(1, 5)]
    public int Criticality { get; set; } = 3;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public virtual OrgUnit? OrgUnit { get; set; }
}
