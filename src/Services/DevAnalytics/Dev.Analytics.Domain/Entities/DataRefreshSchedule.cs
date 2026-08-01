using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Dev.Analytics.Domain.Entities;

[Display(Name = "جدول تحديث البيانات")]
public class DataRefreshSchedule : BaseEntity
{
    [Display(Name = "معرّف نموذج القراءة")]
    public Guid? TargetReadModelId { get; set; }

    [Display(Name = "تعبير Cron")]
    public string? CronExpression { get; set; }

    [Display(Name = "آخر تشغيل")]
    public DateTime? LastRunAt { get; set; }

    [Display(Name = "التشغيل التالي")]
    public DateTime? NextRunAt { get; set; }

    public virtual ReadModel? TargetReadModel { get; set; }
}
