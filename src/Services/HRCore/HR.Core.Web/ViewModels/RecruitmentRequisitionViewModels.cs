using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.ViewModels;
using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Web.ViewModels;

public class RecruitmentRequisitionsIndexViewModel : SearchFilterBase
{
    public RequisitionStatus? Status { get; set; }
    public Guid? OrgUnitId { get; set; }
    public int? BudgetYear { get; set; }
    public string? JobTitle { get; set; }
    public IReadOnlyList<RecruitmentRequisition> Items { get; set; } = [];
}

public class RecruitmentRequisitionCreateViewModel
{
    [Required(ErrorMessage = "حدّد الفرع أو المركزية")]
    [Display(Name = "الفرع / المركزية")]
    public Guid ScopeOrgUnitId { get; set; }

    [Required(ErrorMessage = "المنصب الممول مطلوب")]
    [Display(Name = "المنصب الممول الشاغر")]
    public Guid FundedPositionId { get; set; }

    [Required(ErrorMessage = "المسمى الوظيفي مطلوب")]
    [Display(Name = "المسمى الوظيفي")]
    [StringLength(200)]
    public string JobTitle { get; set; } = string.Empty;

    [Display(Name = "مبررات الطلب")]
    [StringLength(500)]
    public string? JustificationNotes { get; set; }

    [Display(Name = "مرجع قانوني")]
    [StringLength(100)]
    public string? LegalSourceId { get; set; }

    [Display(Name = "سنة الموازنة")]
    public int BudgetYear { get; set; } = DateTime.UtcNow.Year;

    [Display(Name = "قناة النشر")]
    public RecruitmentPublishingChannel PublishingChannel { get; set; } = RecruitmentPublishingChannel.PublicOnly;

    /// <summary>HCM-026: optional in Draft; required (≥ today + MinPostingDays) at first publish.</summary>
    [Display(Name = "تاريخ إغلاق التقديم")]
    [DataType(DataType.Date)]
    public DateOnly? ApplicationClosingDate { get; set; }

    /// <summary>HCM-028 (A1): defaults from <c>Recruitment:CommitteeApprovalDefault</c>.</summary>
    [Display(Name = "يتطلب اعتماد لجنة الاختيار قبل الترتيب")]
    public bool RequiresCommitteeApproval { get; set; }

    // HCM-030 (D2): application criteria (null = not checked).

    [Display(Name = "أدنى مؤهل علمي")]
    [StringLength(20)]
    public string? MinQualificationCode { get; set; }

    [Display(Name = "أدنى سنوات خبرة")]
    [Range(typeof(decimal), "0", "60", ErrorMessage = "سنوات الخبرة يجب أن تكون بين 0 و60.")]
    public decimal? MinYearsOfExperience { get; set; }

    [Display(Name = "الحد الأدنى للعمر")]
    [Range(14, 100, ErrorMessage = "العمر يجب أن يكون بين 14 و100.")]
    public int? MinAge { get; set; }

    [Display(Name = "الحد الأعلى للعمر")]
    [Range(14, 100, ErrorMessage = "العمر يجب أن يكون بين 14 و100.")]
    public int? MaxAge { get; set; }

    /// <summary>HCM-030 (E4): <c>CandidateDocumentTypes</c> codes the candidate is asked to attach.</summary>
    [Display(Name = "المستندات المطلوبة من المتقدّم")]
    public List<string> RequiredDocumentTypes { get; set; } = [];
}

public class RecruitmentRequisitionEditViewModel : RecruitmentRequisitionCreateViewModel
{
    public Guid Id { get; set; }
}

public record VacantPositionOption(
    Guid Id,
    string PositionCode,
    string GradeCode,
    int BudgetYear,
    string? JobTitle,
    string? OrgUnitName);

public class RecruitmentRequisitionDetailsViewModel
{
    public RecruitmentRequisition Requisition { get; set; } = null!;
    public WorkflowPanelViewModel Workflow { get; set; } = new();
    /// <summary>Publish/submit/edit/cancel require HrCore.Write — hidden for approver-only users.</summary>
    public bool CanWrite { get; set; }
    /// <summary>HCM-026: cancelling an Approved/Published requisition needs HrCoreApprove.</summary>
    public bool CanApprove { get; set; }
    /// <summary>HCM-026: assessment criteria versions panel.</summary>
    public AssessmentCriteriaPanelViewModel CriteriaPanel { get; set; } = new();
    /// <summary>HCM-027 (F3): next candidate by merit after a decline/expiry/revoke (null when none).</summary>
    public HR.Core.Infrastructure.Services.Recruitment.NextCandidateSuggestion? NextCandidate { get; set; }
    /// <summary>HCM-028: latest committee session on the requisition (any status), or null.</summary>
    public SelectionCommitteeSession? LatestCommitteeSession { get; set; }
    /// <summary>HCM-028: committee gate and latest approved Ranking/Final session.</summary>
    public HR.Core.Infrastructure.Services.Recruitment.CommitteeRankingGate? CommitteeGate { get; set; }
    /// <summary>HCM-028 (A1): the committee requirement is frozen once any application holds a rank.</summary>
    public bool RankingStarted { get; set; }
}

public class RequisitionWorkflowPanelViewModel : WorkflowPanelViewModel;

public class RequisitionWorkflowStepViewModel : WorkflowStepViewModel;

public class RequisitionWorkflowActionViewModel
{
    public Guid RequisitionId { get; set; }

    [Display(Name = "ملاحظات")]
    [StringLength(500)]
    public string? Comments { get; set; }
}
