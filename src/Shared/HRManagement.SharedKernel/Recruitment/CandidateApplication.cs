using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Recruitment;

/// <summary>HCM-001: a candidate's application for a published requisition.</summary>
public class CandidateApplication : BaseEntity
{
    public Guid RequisitionId { get; set; }

    [Required, MaxLength(100)]
    public string CandidateName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? NationalId { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Submitted;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ScreenedAt { get; set; }

    public decimal? AssessmentScore { get; set; }

    public int? Rank { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool ConflictOfInterestDeclared { get; set; }
}

public enum ApplicationStatus
{
    [Display(Name = "مُرسَل")]
    Submitted = 0,
    [Display(Name = "قيد الفرز")]
    UnderScreening = 1,
    [Display(Name = "في القائمة المختصرة")]
    Shortlisted = 2,
    [Display(Name = "مقابلة مجدولة")]
    InterviewScheduled = 3,
    [Display(Name = "مُقيَّم")]
    Assessed = 4,
    [Display(Name = "مُصنَّف")]
    Ranked = 5,
    [Display(Name = "عرض مُقدَّم")]
    OfferExtended = 6,
    [Display(Name = "مقبول")]
    Accepted = 7,
    [Display(Name = "مرفوض")]
    Rejected = 8,
    [Display(Name = "منسحب")]
    Withdrawn = 9
}
