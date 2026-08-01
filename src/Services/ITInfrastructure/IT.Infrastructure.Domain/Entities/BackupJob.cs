using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using IT.Infrastructure.Domain.Enums;

namespace IT.Infrastructure.Domain.Entities;

[Display(Name = "مهمة النسخ الاحتياطي")]
public class BackupJob : AuditableEntity
{
    [Required(ErrorMessage = "الهدف مطلوب")]
    [Display(Name = "الهدف")]
    public string Target { get; set; } = default!;

    [Display(Name = "الجدول")]
    public string? Schedule { get; set; }

    [Display(Name = "آخر تشغيل")]
    public DateTime? LastRunAt { get; set; }

    [Display(Name = "الحالة")]
    public BackupStatus Status { get; set; }
}
