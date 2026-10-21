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

    [Display(Name = "نمط المقابلة")]
    public InterviewMode Mode { get; set; } = InterviewMode.InPerson;

    [Display(Name = "تاريخ الإكمال")]
    public DateTime? CompletedAt { get; set; }

    [Display(Name = "النتيجة")]
    public InterviewOutcome? Outcome { get; set; }

    [StringLength(2000)]
    [Display(Name = "ملاحظات النتيجة")]
    public string? OutcomeNotes { get; set; }

    [StringLength(500)]
    [Display(Name = "سبب الإلغاء أو إعادة الجدولة")]
    public string? CancelledReason { get; set; }

    /// <summary>HCM-026: previous slot in the reschedule chain.</summary>
    [Display(Name = "مُعاد جدولتها من")]
    public Guid? RescheduledFromSlotId { get; set; }

    public virtual RecruitmentInterviewSlot? RescheduledFromSlot { get; set; }

    public virtual ICollection<RecruitmentInterviewPanelMember> PanelMembers { get; set; } = [];
}
