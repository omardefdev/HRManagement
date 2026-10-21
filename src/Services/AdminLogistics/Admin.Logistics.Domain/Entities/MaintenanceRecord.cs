using System.ComponentModel.DataAnnotations;
using Admin.Logistics.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Admin.Logistics.Domain.Entities;

[Display(Name = "سجل الصيانة")]
public class MaintenanceRecord : BaseEntity
{
    [Display(Name = "الأصل")]
    [Required(ErrorMessage = "يجب اختيار الأصل")]
    public Guid AssetId { get; set; }

    [Display(Name = "نوع الصيانة")]
    public MaintenanceType MaintenanceType { get; set; }

    [Display(Name = "تاريخ التنفيذ")]
    public DateTime? PerformedAt { get; set; }

    [Display(Name = "نفذ بواسطة")]
    public string? PerformedBy { get; set; }

    [Display(Name = "التكلفة")]
    public decimal? Cost { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public Asset? Asset { get; set; }
}
