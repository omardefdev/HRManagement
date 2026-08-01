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
    Submitted = 0,
    UnderScreening = 1,
    Shortlisted = 2,
    InterviewScheduled = 3,
    Assessed = 4,
    Ranked = 5,
    OfferExtended = 6,
    Accepted = 7,
    Rejected = 8,
    Withdrawn = 9
}
