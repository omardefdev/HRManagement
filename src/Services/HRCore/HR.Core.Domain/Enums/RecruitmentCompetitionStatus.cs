using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>
/// HCM-031 (PLAN-31): lifecycle of a multi-vacancy competition (stored as string). The objection window is a period
/// after the first publication (<c>ResultsFirstPublishedAt + ObjectionWindowDays</c>), not a status.
/// </summary>
public enum RecruitmentCompetitionStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval = 1,
    [Display(Name = "معتمدة")]
    Approved = 2,
    [Display(Name = "معلنة")]
    Announced = 3,
    [Display(Name = "أغلق التقديم")]
    IntakeClosed = 4,
    [Display(Name = "قيد الامتحان")]
    Examining = 5,
    [Display(Name = "مسودة النتائج")]
    ResultsDraft = 6,
    [Display(Name = "النتائج بانتظار الاعتماد")]
    ResultsPendingApproval = 7,
    [Display(Name = "النتائج منشورة")]
    ResultsPublished = 8,
    [Display(Name = "منتهية")]
    Finalized = 9,
    [Display(Name = "ملغاة")]
    Cancelled = 10
}

/// <summary>
/// HCM-031: how a reconciled paper's exam score is formed once the reconciler has marked it (stored as string). Within
/// the threshold the score is always the average of the first and second marks.
/// </summary>
public enum CompetitionReconcileRule
{
    /// <summary>Average of the three marks.</summary>
    [Display(Name = "متوسط العلامات الثلاث")]
    Average = 0,
    /// <summary>The reconciler's mark is final.</summary>
    [Display(Name = "قرار المصحح الثالث")]
    ReconcilerDecides = 1,
    /// <summary>Average of the two closest of the three marks (ties: the pair with the higher sum).</summary>
    [Display(Name = "متوسط أقرب علامتين")]
    AverageOfClosestTwo = 2
}

/// <summary>HCM-031: fixed set of tie-break criteria; a competition stores an ordered subset (stored as text).</summary>
public enum CompetitionTieBreak
{
    [Display(Name = "علامة الامتحان")]
    ExamScore = 0,
    [Display(Name = "علامة المقابلة")]
    InterviewScore = 1,
    [Display(Name = "سنوات الخبرة")]
    YearsOfExperience = 2,
    [Display(Name = "أسبقية التقديم")]
    AppliedAt = 3
}
