using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Web.ViewModels;

public class RecruitmentApplicationDetailsViewModel
{
    public RecruitmentApplication Application { get; set; } = null!;
    public IReadOnlyList<RecruitmentApplication> RankBoard { get; set; } = [];
    public IReadOnlyList<RecruitmentApplicationStatus> AllowedStatusTransitions { get; set; } = [];
    /// <summary>HCM-026: structured scoring sheet (replaces the free-form score form).</summary>
    public AssessmentScoringPanelViewModel ScoringPanel { get; set; } = new();
    public bool LegacyHireWithoutOffer { get; set; }
    public bool CanHire { get; set; }
    public bool AppealPending { get; set; }
    public bool CanRank { get; set; } = true;
    public bool CanOffer { get; set; } = true;
    public bool CanAppeal { get; set; }
    /// <summary>HCM-028 (G3): why a rejected application cannot be appealed (window, kind, earlier appeal).</summary>
    public string? AppealFilingError { get; set; }
    /// <summary>HCM-028 (F3): latest appeal; handled on RecruitmentAppeals/Details.</summary>
    public RecruitmentAppeal? ActiveAppeal { get; set; }
    /// <summary>HCM-028 (I): a pending appeal on any application of the requisition blocks the hire.</summary>
    public string? HireBlockedReason { get; set; }
    public IReadOnlyList<RecruitmentInterviewSlot> InterviewSlots { get; set; } = [];
    /// <summary>HCM-027: lowest rank that can still receive an offer on the requisition (merit order).</summary>
    public int? BestAvailableRank { get; set; }
    /// <summary>HCM-027: this application is not next by merit — an offer needs a merit override.</summary>
    public bool RequiresMeritOverride { get; set; }
    /// <summary>HCM-027: default for «مدة صلاحية العرض (أيام)» (Recruitment:OfferValidityDays).</summary>
    public int DefaultOfferValidityDays { get; set; } = 14;
    /// <summary>HCM-027 (E5): grade of the funded position — pre-fills the offer grade.</summary>
    public string? PositionGradeCode { get; set; }
    /// <summary>HCM-027 (E5): Payroll bounds of the position grade at the default start date (null when not looked up).</summary>
    public decimal? GradeMinSalary { get; set; }
    public decimal? GradeMaxSalary { get; set; }
    /// <summary>HCM-027 (E5): the Payroll lookup was unavailable — a salary justification will be required.</summary>
    public bool PayrollLookupUnavailable { get; set; }
    /// <summary>HCM-027 (E5): Payroll answered that the position grade is not in the active salary scale.</summary>
    public bool PositionGradeNotInScale { get; set; }
    /// <summary>HCM-027 (E4): default proposed start date for the offer form.</summary>
    public DateTime DefaultProposedStartDate { get; set; } = DateTime.UtcNow.Date.AddDays(30);
    /// <summary>HCM-028: committee gate of the requisition (null without a requisition).</summary>
    public HR.Core.Infrastructure.Services.Recruitment.CommitteeRankingGate? CommitteeGate { get; set; }
    /// <summary>HCM-028: this candidate's decision in the latest approved Ranking/Final session, or null.</summary>
    public SelectionCommitteeDecision? CommitteeDecision => CommitteeGate?.DecisionFor(Application.Id);
    /// <summary>HCM-030 (E6): candidate documents with their File.Storage scan state.</summary>
    public IReadOnlyList<HR.Core.Web.Services.CandidateDocumentRow> Documents { get; set; } = [];
    /// <summary>HCM-030 (E6): required document types of the requisition still missing.</summary>
    public IReadOnlyList<string> MissingDocumentTypes { get; set; } = [];
    /// <summary>COM-030 (F7): candidate messages (masked recipient; the body is not shown to HR here), newest first.</summary>
    public IReadOnlyList<CandidateNotificationRow> Notifications { get; set; } = [];
}

/// <summary>COM-030 (F7): one row of the «الإشعارات» card.</summary>
public sealed record CandidateNotificationRow(
    string TemplateCode,
    HRManagement.SharedKernel.Notifications.NotificationChannel Channel,
    CandidateNotificationStatus Status,
    string RecipientMasked,
    DateTime CreatedAt,
    DateTime? SentAt,
    int Attempts,
    string? LastError);

public class AssignRankForm
{
    public Guid ApplicationId { get; set; }

    [Display(Name = "الترتيب")]
    [Range(1, 9999, ErrorMessage = "الترتيب يجب أن يكون موجباً.")]
    public int Rank { get; set; }

    /// <summary>HCM-028: required (20+ chars) when the rank differs from the approved committee's proposed rank.</summary>
    [Display(Name = "سبب مخالفة الترتيب المقترح")]
    [StringLength(1000)]
    public string? RankOverrideReason { get; set; }
}

public class CreateOfferForm
{
    public Guid ApplicationId { get; set; }

    [Display(Name = "الراتب المعروض")]
    [Range(0.01, double.MaxValue, ErrorMessage = "الراتب مطلوب.")]
    public decimal OfferedSalary { get; set; }

    [Display(Name = "الدرجة")]
    [StringLength(50)]
    public string? GradeCode { get; set; }

    [Display(Name = "تاريخ المباشرة المقترح")]
    [DataType(DataType.Date)]
    public DateTime ProposedStartDate { get; set; } = DateTime.UtcNow.Date.AddDays(30);

    /// <summary>HCM-027: the expiry date is computed when the offer is issued.</summary>
    [Display(Name = "مدة صلاحية العرض (أيام)")]
    [Range(3, 60, ErrorMessage = "مدة صلاحية العرض يجب أن تكون بين 3 و60 يوماً.")]
    public int? ValidityDays { get; set; }

    [Display(Name = "تجاوز الجدارة")]
    public bool IsMeritOverride { get; set; }

    [Display(Name = "مبرر تجاوز الجدارة")]
    [StringLength(1000)]
    public string? MeritOverrideReason { get; set; }

    /// <summary>HCM-027 (E1): required when the grade differs from the funded position.</summary>
    [Display(Name = "مبرر اختلاف الدرجة")]
    [StringLength(500)]
    public string? GradeDeviationReason { get; set; }

    /// <summary>HCM-027 (E3): required when the Payroll grade lookup is unavailable.</summary>
    [Display(Name = "مبرر الراتب")]
    [StringLength(1000)]
    public string? SalaryJustification { get; set; }
}

public class OfferLettersIndexViewModel
{
    public IReadOnlyList<OfferLetter> Offers { get; set; } = [];
    public OfferStatus? Status { get; set; }
    public string? SearchTerm { get; set; }
    /// <summary>HCM-027: only exception offers (merit override, grade deviation, unverified salary).</summary>
    public bool MeritOverrideOnly { get; set; }
}

public class OfferLetterDetailsViewModel
{
    public OfferLetter Offer { get; set; } = null!;
    public bool CanSubmit { get; set; }
    public bool CanAccept { get; set; }
    public bool CanDecline { get; set; }
    public bool CanRevoke { get; set; }
    public WorkflowPanelViewModel Workflow { get; set; } = new();
    /// <summary>HCM-027 (F3): next candidate by merit after a decline/expiry/revoke (null when none).</summary>
    public HR.Core.Infrastructure.Services.Recruitment.NextCandidateSuggestion? NextCandidate { get; set; }
    /// <summary>HCM-029 (D2): the official letter as rendered now, with the hash stored at issue.</summary>
    public HR.Core.Infrastructure.Services.Recruitment.OfferLetterDocumentView? Document { get; set; }
}

/// <summary>HCM-027: decline recorded by HR on the candidate's behalf (Candidate is reserved for PLAN-30).</summary>
public class DeclineOfferForm
{
    [Display(Name = "سبب الرفض")]
    [StringLength(1000)]
    public string? Reason { get; set; }
}

public class RevokeOfferForm
{
    [Display(Name = "سبب الإلغاء")]
    [StringLength(1000)]
    public string? Reason { get; set; }

    [Display(Name = "خطأ إداري — إعادة المرشح إلى المرتبين")]
    public bool RevertToRanked { get; set; }
}

public class OfferLetterWorkflowActionViewModel
{
    public Guid OfferId { get; set; }

    [Display(Name = "ملاحظات")]
    [StringLength(500)]
    public string? Comments { get; set; }
}

public class SelectionCommitteeDetailsViewModel
{
    public SelectionCommitteeSession Session { get; set; } = null!;
    public bool CanSubmit { get; set; }
    public WorkflowPanelViewModel Workflow { get; set; } = new();

    /// <summary>HCM-028: members, per-candidate conflicts, votes, decisions and candidates (set by the controller).</summary>
    public HR.Core.Infrastructure.Services.Recruitment.SelectionCommitteeWorkspace? Workspace { get; set; }
}

public class SelectionCommitteeWorkflowActionViewModel
{
    public Guid SessionId { get; set; }

    [Display(Name = "ملاحظات")]
    [StringLength(500)]
    public string? Comments { get; set; }
}
