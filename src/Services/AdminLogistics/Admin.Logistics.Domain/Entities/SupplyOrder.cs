using System.ComponentModel.DataAnnotations;
using Admin.Logistics.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Admin.Logistics.Domain.Entities;

[Display(Name = "طلب تجهيز")]
public class SupplyOrder : AuditableEntity
{
    [Display(Name = "طالب الطلب")]
    public Guid? RequestedBy { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "التفاصيل")]
    public string? Items { get; set; }

    [Display(Name = "الحالة")]
    public SupplyOrderStatus Status { get; set; }

    [Display(Name = "تاريخ الطلب")]
    public DateTime? OrderedAt { get; set; }

    [Display(Name = "تاريخ التنفيذ")]
    public DateTime? FulfilledAt { get; set; }
}
