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

    [Display(Name = "قائد التفتيش")]
    public Guid? LeadInspectorId { get; set; }

    [Display(Name = "الملخص")]
    public string? Summary { get; set; }
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
}
