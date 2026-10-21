using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-006: interview scheduling for recruitment applications.</summary>
public class RecruitmentInterviewSlot : AuditableEntity
{
    [Display(Name = "طلب التوظيف")]
    public Guid RecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? RecruitmentApplication { get; set; }

    [Display(Name = "طلب المواطن")]
    public Guid? CitizenApplicationId { get; set; }

    public virtual CitizenApplication? CitizenApplication { get; set; }

    [Display(Name = "موعد المقابلة")]
    public DateTime ScheduledAt { get; set; }

    [StringLength(200)]
    [Display(Name = "المكان")]
    public string? Location { get; set; }

    [Display(Name = "ملاحظات اللجنة")]
    public string? PanelNotes { get; set; }

    [Display(Name = "الحالة")]
    public InterviewSlotStatus Status { get; set; } = InterviewSlotStatus.Scheduled;
}
