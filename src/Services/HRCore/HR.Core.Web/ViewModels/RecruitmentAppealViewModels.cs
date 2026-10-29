using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Web.ViewModels;

/// <summary>HCM-028 (F2): one row of RecruitmentAppeals/Index.</summary>
public sealed record RecruitmentAppealListItem(
    RecruitmentAppeal Appeal,
    string ApplicantFullName,
    string? RequisitionNumber,
    string? ReviewerName);

public sealed class RecruitmentAppealsIndexViewModel
{
    public IReadOnlyList<RecruitmentAppealListItem> Items { get; init; } = [];
    /// <summary><c>pending</c> (default) or <c>all</c>.</summary>
    public string StatusFilter { get; init; } = "pending";
    /// <summary>A compliance-only user sees the appeals assigned to them.</summary>
    public bool AssignedOnly { get; init; }
}

/// <summary>
/// HCM-028 (F2): RecruitmentAppeals/Details — the appeal, a summary of the application without the national ID,
/// the reviewer assignment and the explicit decision forms.
/// </summary>
public sealed class RecruitmentAppealDetailsViewModel
{
    public RecruitmentAppeal Appeal { get; init; } = null!;
    public RecruitmentApplication Application { get; init; } = null!;
    public RecruitmentRequisition? Requisition { get; init; }
    public string? ReviewerName { get; init; }
    public int AssessorCount { get; init; }
    public SelectionCommitteeDecision? CommitteeDecision { get; init; }
    public WorkflowPanelViewModel? Workflow { get; init; }

    /// <summary>HR (HrCoreWrite) assigns or, while pending, reassigns the reviewer.</summary>
    public bool CanAssign { get; init; }
    /// <summary>The current user is the assigned compliance reviewer and the review is not waiting for information.</summary>
    public bool CanDecide { get; init; }
    /// <summary>HR (HrCoreWrite) answers the reviewer's information request.</summary>
    public bool CanResubmit { get; init; }
    public IReadOnlyList<RecruitmentAppealOutcome> AllowedOutcomes { get; init; } = [];
    public bool CanCorrectRank => AllowedOutcomes.Contains(RecruitmentAppealOutcome.UpheldRankCorrection);
    public int? RankBeforeRejection => Application.RankBeforeRejection ?? Application.Rank;

    /// <summary>HCM-031 (PLAN-31): a competition result objection (decided with a corrected exam mark or dismissed).</summary>
    public bool IsObjection => Appeal.Kind == RecruitmentAppealKind.ResultObjection;
    /// <summary>HCM-031: the objected paper — exam number, marks by round, the closed mark and the maximum.</summary>
    public ObjectionPaperView? ObjectionPaper { get; init; }
}

/// <summary>HCM-031: the paper of a result objection as the independent reviewer sees it.</summary>
public sealed record ObjectionPaperView(string ExamNumber, decimal MaxScore, decimal? First, decimal? Second, decimal? Reconciler, decimal? FinalMark);
