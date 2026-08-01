using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using IT.Infrastructure.Domain.Enums;

namespace IT.Infrastructure.Domain.Entities;

[Display(Name = "سيرفر / جهاز")]
public class ServerDevice : AuditableEntity
{
    [Required(ErrorMessage = "اسم المضيف مطلوب")]
    [Display(Name = "اسم المضيف")]
    public string Hostname { get; set; } = default!;

    [Display(Name = "عنوان IP")]
    public string? IpAddress { get; set; }

    [Display(Name = "نظام التشغيل")]
    public string? Os { get; set; }

    [Display(Name = "الغرض")]
    public string? Purpose { get; set; }

    [Display(Name = "الموقع")]
    public string? Location { get; set; }

    [Display(Name = "الحالة")]
    public ServerStatus Status { get; set; }
}
