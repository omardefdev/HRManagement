using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-031 (PLAN-31): one exam session (hall) of a competition.</summary>
public class ExamSession : AuditableEntity
{
    public Guid CompetitionId { get; set; }

    public virtual RecruitmentCompetition? Competition { get; set; }

    [Display(Name = "نوع الامتحان")]
    public ExamSessionType Type { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "عنوان الجلسة")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الموعد")]
    public DateTime ScheduledAt { get; set; }

    [Display(Name = "المدة (دقائق)")]
    public int DurationMinutes { get; set; }

    [Required]
    [StringLength(300)]
    [Display(Name = "المكان")]
    public string Venue { get; set; } = string.Empty;

    [Display(Name = "السعة")]
    public int Capacity { get; set; }

    [Display(Name = "العلامة العظمى")]
    public decimal MaxScore { get; set; }

    [Display(Name = "الحالة")]
    public ExamSessionStatus Status { get; set; } = ExamSessionStatus.Planned;

    [Display(Name = "تاريخ إغلاق التصحيح")]
    public DateTime? MarkingClosedAt { get; set; }

    public virtual ICollection<ExamCandidate> Candidates { get; set; } = [];

    public virtual ICollection<ExamMarkerAssignment> Markers { get; set; } = [];
}

/// <summary>
/// HCM-031: a seated application in a session. The exam number is random, unique per competition and the same for the
/// application on every exam type; markers see only the exam number (never the application).
/// </summary>
public class ExamCandidate : AuditableEntity
{
    public Guid ExamSessionId { get; set; }

    public virtual ExamSession? ExamSession { get; set; }

    /// <summary>Denormalized from the session (uniqueness of exam numbers per competition and type).</summary>
    public Guid CompetitionId { get; set; }

    public ExamSessionType ExamType { get; set; }

    public Guid RecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? RecruitmentApplication { get; set; }

    [Required]
    [StringLength(16)]
    [Display(Name = "الرقم الامتحاني")]
    public string ExamNumber { get; set; } = string.Empty;

    [Display(Name = "رقم المقعد")]
    public int SeatNumber { get; set; }

    [Display(Name = "الحضور")]
    public ExamAttendance Attendance { get; set; } = ExamAttendance.Unknown;

    /// <summary>The exam mark formed from the marking rounds when marking is closed (null: absent or not closed yet).</summary>
    [Display(Name = "علامة الامتحان")]
    public decimal? FinalMark { get; set; }

    [StringLength(64)]
    [Display(Name = "بصمة بطاقة الامتحان")]
    public string? AdmissionCardSha256 { get; set; }

    [Display(Name = "تاريخ إصدار البطاقة")]
    public DateTime? AdmissionCardIssuedAt { get; set; }

    public virtual ICollection<ExamScore> Scores { get; set; } = [];
}

/// <summary>
/// HCM-031: a marker assigned to a session in one role. Markers are assigned by employee; the token subject is bound on
/// the marker's first visit (<see cref="MarkerUserId"/>). Distinct people per role on a session.
/// </summary>
public class ExamMarkerAssignment : AuditableEntity
{
    public Guid ExamSessionId { get; set; }

    public virtual ExamSession? ExamSession { get; set; }

    [Display(Name = "المستخدم")]
    public Guid? MarkerUserId { get; set; }

    [Display(Name = "الموظف")]
    public Guid? MarkerEmployeeId { get; set; }

    public virtual Employee? MarkerEmployee { get; set; }

    [Display(Name = "الدور")]
    public ExamMarkerRole Role { get; set; }

    /// <summary>Set when the marker's token also carried HR.Core read roles (warning «يملك هذا المصحح صلاحية الاطلاع على هوية المتقدمين»).</summary>
    [Display(Name = "يملك صلاحية اطلاع")]
    public bool HoldsHrReadAccess { get; set; }
}

/// <summary>HCM-031: one marker's mark for one paper in one round (blind: other rounds are never shown to First/Second).</summary>
public class ExamScore : AuditableEntity
{
    public Guid ExamCandidateId { get; set; }

    public virtual ExamCandidate? ExamCandidate { get; set; }

    [Display(Name = "المصحح")]
    public Guid MarkerUserId { get; set; }

    [Display(Name = "الجولة")]
    public ExamScoreRound Round { get; set; }

    [Display(Name = "العلامة")]
    public decimal Score { get; set; }

    [Display(Name = "تاريخ الإدخال")]
    public DateTime SubmittedAt { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظة")]
    public string? Note { get; set; }

    /// <summary>P4: the objection (appeal) an <see cref="ExamScoreRound.ObjectionCorrection"/> answers.</summary>
    public Guid? RecruitmentAppealId { get; set; }
}
