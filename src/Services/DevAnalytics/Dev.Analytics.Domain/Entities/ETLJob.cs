using System.ComponentModel.DataAnnotations;
using Dev.Analytics.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Dev.Analytics.Domain.Entities;

[Display(Name = "مهمة ETL")]
public class ETLJob : AuditableEntity
{
    [Display(Name = "الاسم")]
    [Required(ErrorMessage = "الاسم مطلوب")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "الجدولة")]
    public string? Schedule { get; set; }

    [Display(Name = "آخر تشغيل")]
    public DateTime? LastRun { get; set; }

    [Display(Name = "الحالة")]
    public JobStatus Status { get; set; }

    [Display(Name = "السجل")]
    public string? Log { get; set; }
}
