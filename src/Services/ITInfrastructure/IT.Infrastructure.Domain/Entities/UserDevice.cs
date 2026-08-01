using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using IT.Infrastructure.Domain.Enums;

namespace IT.Infrastructure.Domain.Entities;

[Display(Name = "جهاز المستخدم")]
public class UserDevice : AuditableEntity
{
    [Required]
    [Display(Name = "رقم الأصل")]
    public string AssetTag { get; set; } = default!;

    [Display(Name = "مخصص لـ")]
    public Guid? AssignedTo { get; set; }

    [Display(Name = "نوع الجهاز")]
    public DeviceType DeviceType { get; set; }

    [Display(Name = "الرقم التسلسلي")]
    public string? SerialNumber { get; set; }

    [Display(Name = "الحالة")]
    public DeviceStatus Status { get; set; }
}
