using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;

namespace HR.Core.Web.ViewModels;

public class WorkforcePlanCreateViewModel
{
    [Required]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "السنة المالية")]
    public int FiscalYear { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "ملخص")]
    public string? Summary { get; set; }
}

public class HrStatisticReportCreateViewModel
{
    [Required]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "بداية الفترة")]
    public DateOnly PeriodStart { get; set; }

    [Display(Name = "نهاية الفترة")]
    public DateOnly PeriodEnd { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "ملخص المؤشرات")]
    public string? MetricsSummary { get; set; }
}

public class DailyAttendanceRecordCreateViewModel
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "اليوم")]
    public DateOnly AttendanceDate { get; set; }

    [Display(Name = "الحالة")]
    public DailyAttendanceStatus Status { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

public class DailyAttendanceRecordEditViewModel : DailyAttendanceRecordCreateViewModel
{
    public Guid Id { get; set; }
}

public class FieldInspectionVisitCreateViewModel
{
    [Required]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الموقع")]
    public string? Location { get; set; }

    [Display(Name = "تاريخ الزيارة")]
    public DateOnly VisitDate { get; set; }

    // PLAN-52: the inspector may belong to any unit; the visit's own unit is OrgUnitId.
    [HRManagement.ServiceDefaults.NotOrgScopeKey("the inspector: an employee of any unit; the visit's unit is OrgUnitId")]
    [Display(Name = "قائد التفتيش")]
    public Guid? LeadInspectorId { get; set; }

    [Display(Name = "الملخص")]
    public string? Summary { get; set; }

    /// <summary>PLAN-52 (D-52-2): a restricted creator's visit takes his scope root; a central user chooses (empty = central).</summary>
    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }
}

public class DiwanCorrespondenceCreateViewModel
{
    [Required]
    [Display(Name = "المرجع")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "الموضوع")]
    public string Subject { get; set; } = string.Empty;

    [Display(Name = "الاتجاه")]
    public CorrespondenceDirection Direction { get; set; }

    [Display(Name = "الجهة")]
    public string? ExternalParty { get; set; }

    [Display(Name = "نص / ملخص")]
    public string? Body { get; set; }

    [Display(Name = "موظف مرتبط")]
    public Guid? RelatedEmployeeId { get; set; }

    /// <summary>PLAN-52 (D-52-2): the related employee's unit, else a restricted creator's scope root, else this choice (empty = central).</summary>
    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }
}
