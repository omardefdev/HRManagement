using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-031 (PLAN-31): status of a results version (stored as string).</summary>
public enum CompetitionResultVersionStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval = 1,
    [Display(Name = "معتمدة")]
    Approved = 2,
    [Display(Name = "منشورة")]
    Published = 3,
    [Display(Name = "مستبدلة")]
    Superseded = 4
}

/// <summary>HCM-031: outcome of one candidate in a results version (stored as string).</summary>
public enum CompetitionResultOutcome
{
    [Display(Name = "ناجح")]
    Passed = 0,
    [Display(Name = "راسب")]
    Failed = 1,
    [Display(Name = "غائب")]
    Absent = 2
}

/// <summary>
/// HCM-031 (PLAN-31, AC-HCM-31d/31e): one computed version of the results. A draft is approved through
/// <c>RECRUITMENT-RESULTS-APPROVAL</c>, then published; a later version (upheld objection) supersedes the published one.
/// The SHA-256 is of the canonical JSON of its results; published results are never changed.
/// </summary>
public class CompetitionResultVersion : AuditableEntity
{
    public Guid CompetitionId { get; set; }

    public virtual RecruitmentCompetition? Competition { get; set; }

    [Display(Name = "رقم النسخة")]
    public int VersionNo { get; set; }

    [Display(Name = "الحالة")]
    public CompetitionResultVersionStatus Status { get; set; } = CompetitionResultVersionStatus.Draft;

    [Display(Name = "تاريخ الاحتساب")]
    public DateTime ComputedAt { get; set; }

    [Display(Name = "احتسبها")]
    public Guid? ComputedBy { get; set; }

    [Display(Name = "تاريخ النشر")]
    public DateTime? PublishedAt { get; set; }

    [StringLength(1000)]
    [Display(Name = "سبب النسخة")]
    public string? Reason { get; set; }

    [Display(Name = "مسار الاعتماد")]
    public Guid? WorkflowInstanceId { get; set; }

    [StringLength(64)]
    [Display(Name = "البصمة")]
    public string? Sha256 { get; set; }

    [StringLength(500)]
    [Display(Name = "سبب الإرجاع")]
    public string? ReturnReason { get; set; }

    /// <summary>Rank and outcome changes against the previously published version (JSON list; null for the first version).</summary>
    public string? DiffJson { get; set; }

    public virtual ICollection<CompetitionResult> Results { get; set; } = [];
}

/// <summary>HCM-031: one candidate's result in a version — an immutable snapshot once published.</summary>
public class CompetitionResult : BaseEntity
{
    public Guid ResultVersionId { get; set; }

    public virtual CompetitionResultVersion? ResultVersion { get; set; }

    public Guid RecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? RecruitmentApplication { get; set; }

    [Required]
    [StringLength(16)]
    public string ExamNumber { get; set; } = string.Empty;

    /// <summary>Exam stage points (exam mark normalized to the exam weight).</summary>
    public decimal? ExamScore { get; set; }

    /// <summary>Interview stage points (tie-break input).</summary>
    public decimal? InterviewScore { get; set; }

    public decimal? FinalScore { get; set; }

    public int? OverallRank { get; set; }

    public CompetitionResultOutcome Outcome { get; set; }

    /// <summary>A tie the tie-break order could not resolve («تعادل يتطلب قراراً»); cleared by an HR tie decision.</summary>
    public bool TieUnresolved { get; set; }

    [StringLength(500)]
    public string? TieDecisionReason { get; set; }
}

/// <summary>HCM-031 (P5): where finalization placed a passed candidate.</summary>
public class CompetitionAllocation : AuditableEntity
{
    public Guid CompetitionId { get; set; }

    public Guid RecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? RecruitmentApplication { get; set; }

    public Guid? AllocatedLineId { get; set; }

    public virtual CompetitionLine? AllocatedLine { get; set; }

    public Guid? AllocatedRequisitionId { get; set; }

    public virtual RecruitmentRequisition? AllocatedRequisition { get; set; }

    /// <summary>The preference order of the allocated line (1 = first choice).</summary>
    public int? PreferenceOrderSatisfied { get; set; }

    /// <summary>True when the allocation came from a reserve recall (P5), not from finalization.</summary>
    public bool FromReserve { get; set; }
}

/// <summary>HCM-031 (P5): status of a line reserve entry (stored as string).</summary>
public enum CompetitionReserveEntryStatus
{
    [Display(Name = "نشط")]
    Active = 0,
    [Display(Name = "مُستدعى")]
    Recalled = 1,
    [Display(Name = "منتهي الصلاحية")]
    Expired = 2,
    [Display(Name = "مُزال")]
    Removed = 3
}

/// <summary>HCM-031 (P5): a passed, unallocated candidate on the reserve list of one preferred line.</summary>
public class CompetitionReserveEntry : AuditableEntity
{
    public Guid CompetitionLineId { get; set; }

    public virtual CompetitionLine? CompetitionLine { get; set; }

    public Guid RecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? RecruitmentApplication { get; set; }

    /// <summary>Position on the line's reserve list (by overall rank).</summary>
    public int LineRank { get; set; }

    public DateOnly ReserveUntil { get; set; }

    public CompetitionReserveEntryStatus Status { get; set; } = CompetitionReserveEntryStatus.Active;

    public DateTime? StatusChangedAt { get; set; }

    /// <summary>The requisition the entry was recalled to.</summary>
    public Guid? RecalledRequisitionId { get; set; }
}
