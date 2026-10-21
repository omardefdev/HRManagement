using System.ComponentModel.DataAnnotations;
using HR.Core.Web.Services;
using HR.Core.Domain.Entities;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.ViewModels;

public record BranchScopeOption(Guid Id, string Label);

public class JobCatalogIndexViewModel : SearchFilterBase
{
    public bool? IsActive { get; set; }
    public string? GradeCode { get; set; }
    public IReadOnlyList<JobCatalogEntry> Items { get; set; } = [];
}

public class JobCatalogFormViewModel
{
    [Display(Name = "رمز الوظيفة")]
    [StringLength(20)]
    public string? JobCode { get; set; }

    [Required(ErrorMessage = "المسمى بالعربي مطلوب")]
    [Display(Name = "المسمى بالعربي")]
    [StringLength(200)]
    public string TitleAr { get; set; } = string.Empty;

    [Display(Name = "المسمى بالإنكليزي")]
    [StringLength(200)]
    public string? TitleEn { get; set; }

    [Display(Name = "الوصف")]
    [StringLength(2000)]
    public string? DescriptionAr { get; set; }

    [Required(ErrorMessage = "رمز الدرجة مطلوب")]
    [Display(Name = "رمز الدرجة")]
    [StringLength(20)]
    public string GradeCode { get; set; } = string.Empty;

    [Display(Name = "المجموعة المهنية")]
    [StringLength(100)]
    public string? OccupationalGroup { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;
}

public class JobCatalogEditViewModel : JobCatalogFormViewModel
{
    public Guid Id { get; set; }
}

public sealed class JobCatalogImportViewModel
{
    [Display(Name = "وضع الاستيراد")]
    public JobCatalogImportMode ImportMode { get; set; } = JobCatalogImportMode.Standard;

    [Display(Name = "ملف CSV")]
    public IFormFile? File { get; set; }
}

public class FundedPositionsIndexViewModel : SearchFilterBase
{
    public int? BudgetYear { get; set; }
    public FundedPositionStatus? Status { get; set; }
    public Guid? OrgUnitId { get; set; }
    public Guid? ScopeOrgUnitId { get; set; }
    public IReadOnlyList<FundedPosition> Items { get; set; } = [];
}

public class FundedPositionCreateViewModel
{
    [Required(ErrorMessage = "حدّد الفرع أو المركزية")]
    [Display(Name = "الفرع / المركزية")]
    public Guid ScopeOrgUnitId { get; set; }

    [Required(ErrorMessage = "سنة الموازنة مطلوبة")]
    [Display(Name = "سنة الموازنة")]
    [Range(2000, 2100)]
    public int BudgetYear { get; set; } = DateTime.UtcNow.Year;

    [Required(ErrorMessage = "الوحدة التنظيمية مطلوبة")]
    [Display(Name = "الوحدة التنظيمية")]
    public Guid OrgUnitId { get; set; }

    [Required(ErrorMessage = "دليل الوظائف مطلوب")]
    [Display(Name = "دليل الوظائف")]
    public Guid JobCatalogEntryId { get; set; }

    [Required(ErrorMessage = "رمز الدرجة مطلوب")]
    [Display(Name = "رمز الدرجة")]
    [StringLength(20)]
    public string GradeCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "مكافئ الدوام الكامل مطلوب")]
    [Display(Name = "مكافئ الدوام الكامل")]
    [Range(0.01, 10, ErrorMessage = "أدخل قيمة بين 0.01 و 10")]
    public decimal FTE { get; set; } = 1.0m;
}

public class FundedPositionDetailsViewModel
{
    public FundedPosition Position { get; set; } = null!;
    public IReadOnlyList<RecruitmentRequisition> ActiveRequisitions { get; set; } = [];
    public IReadOnlyList<EstablishmentChangeLog> ChangeLogs { get; set; } = [];
    public bool CanMutate { get; set; }

    [Display(Name = "سبب التجميد")]
    [Required(ErrorMessage = "سبب التجميد مطلوب")]
    [StringLength(500)]
    public string? FreezeReason { get; set; }
}

public class EstablishmentChangesIndexViewModel : SearchFilterBase
{
    public EstablishmentChangeStatus? Status { get; set; }
    public EstablishmentChangeType? ChangeType { get; set; }
    public int? BudgetYear { get; set; }
    public IReadOnlyList<EstablishmentChangeRequest> Items { get; set; } = [];
}

public class EstablishmentChangeSubmitViewModel
{
    [Required(ErrorMessage = "المرجع القانوني مطلوب")]
    [Display(Name = "المرجع القانوني")]
    public string LegalSourceId { get; set; } = string.Empty;

    [Display(Name = "مرجع القرار")]
    [StringLength(100)]
    public string? DecisionReference { get; set; }

    [Display(Name = "تاريخ النفاذ")]
    public DateTime? EffectiveFrom { get; set; }
}

public class EstablishmentChangeDetailsViewModel
{
    public EstablishmentChangeRequest Request { get; set; } = null!;
    public string? LegalSourceLabel { get; set; }
    public IReadOnlyList<EstablishmentChangeLog> ChangeLogs { get; set; } = [];
    public EstablishmentPayloadSummaryViewModel PayloadSummary { get; set; } = new();
    public EstablishmentChangeSubmitViewModel Submit { get; set; } = new();
    public bool CanMutate { get; set; }
    public WorkflowPanelViewModel Workflow { get; set; } = new();
}

public class EstablishmentChangeWorkflowActionViewModel
{
    public Guid ChangeRequestId { get; set; }

    [Display(Name = "ملاحظات")]
    [StringLength(500)]
    public string? Comments { get; set; }
}
