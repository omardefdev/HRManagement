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
