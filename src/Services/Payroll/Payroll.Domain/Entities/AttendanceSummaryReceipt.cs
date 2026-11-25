using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-57 (§4 rule 6, §5): what payroll knows of one employee's attendance summary for one period: the latest version it received,
/// that version's absence, and how much absence it has recorded so far as difference rows. One row per <see cref="SummaryId"/>.
/// It is the reference of «attendance is closed for this employee in this period» (the gate), and the base each new version's
/// difference is computed from: the consumer locks the row, so two versions arriving at once never compute from the same base.
/// </summary>
[Display(Name = "إيصال ملخص حضور")]
public class AttendanceSummaryReceipt : BaseEntity
{
    /// <summary>HR.Core's summary id: stable for (period, employee).</summary>
    public Guid SummaryId { get; set; }

    public Guid AttendancePeriodId { get; set; }

    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "بداية الفترة")]
    public DateOnly PeriodStart { get; set; }

    [Display(Name = "نهاية الفترة")]
    public DateOnly PeriodEnd { get; set; }

    /// <summary>The latest version recorded; an older version that arrives later is ignored.</summary>
    [Display(Name = "آخر نسخة")]
    public int LatestVersion { get; set; }

    /// <summary>The absence of the latest version (absolute).</summary>
    [Display(Name = "أيام الغياب في آخر نسخة")]
    public int LatestAbsentDays { get; set; }

    /// <summary>The sum of the difference rows written for this summary: equal to <see cref="LatestAbsentDays"/> after each version.</summary>
    public int RecordedAbsentDays { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
