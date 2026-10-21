using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Performance;

/// <summary>HCM-003: an individual performance review within a cycle (self, manager, or matrix).</summary>
public class PerformanceReview : BaseEntity
{
    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid PerformanceCycleId { get; set; }

    public Guid ReviewerId { get; set; }

    public ReviewType Type { get; set; } = ReviewType.Manager;

    public ReviewStatus Status { get; set; } = ReviewStatus.NotStarted;

    /// <summary>Overall rating (1-N based on cycle scale).</summary>
    public decimal? OverallRating { get; set; }

    /// <summary>Objectives achievement score (percentage).</summary>
    public decimal? ObjectivesScore { get; set; }

    /// <summary>Competencies score (percentage).</summary>
    public decimal? CompetenciesScore { get; set; }

    /// <summary>Manager's narrative comments (confidential).</summary>
    [MaxLength(4000)]
    public string? Comments { get; set; }

    /// <summary>Employee self-assessment comments.</summary>
    [MaxLength(4000)]
    public string? SelfAssessment { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>Whether an appeal/grievance has been filed.</summary>
    public bool HasAppeal { get; set; }

    /// <summary>Post-calibration adjusted rating (null if unchanged).</summary>
    public decimal? CalibratedRating { get; set; }

    public PerformanceCycle? PerformanceCycle { get; set; }

    public decimal FinalRating => CalibratedRating ?? OverallRating ?? 0;

    public bool IsComplete => Status == ReviewStatus.Completed || Status == ReviewStatus.Acknowledged;
}

public enum ReviewType
{
    [Display(Name = "ذاتي")]
    Self = 0,
    [Display(Name = "مدير")]
    Manager = 1,
    [Display(Name = "مصفوفة")]
    Matrix = 2,
    [Display(Name = "زميل")]
    Peer = 3,
    [Display(Name = "لجنة")]
    Committee = 4
}

public enum ReviewStatus
{
    [Display(Name = "لم يبدأ")]
    NotStarted = 0,
    [Display(Name = "قيد التنفيذ")]
    InProgress = 1,
    [Display(Name = "مُرسَل")]
    Submitted = 2,
    [Display(Name = "مكتمل")]
    Completed = 3,
    [Display(Name = "مُقرّ")]
    Acknowledged = 4,
    [Display(Name = "مستأنف")]
    Appealed = 5
}
