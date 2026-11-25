using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "خصم غياب")]
public class AbsenceDeduction : BaseEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "بداية الفترة")]
    public DateOnly PeriodStart { get; set; }

    [Display(Name = "نهاية الفترة")]
    public DateOnly PeriodEnd { get; set; }

    [Display(Name = "أيام الغياب")]
    public int AbsentDays { get; set; }

    [Display(Name = "المبلغ")]
    public decimal Amount { get; set; }

    [MaxLength(500)]
    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    /// <summary>
    /// Stable key of the upstream fact this deduction represents (for unpaid leave:
    /// <c>LeaveRequestId=&lt;guid&gt;</c>; for an attendance summary version: <c>ATT:{SummaryId}:v{Version}</c>). Carries a filtered unique index so a redelivered or
    /// replayed approval cannot create a second deduction for the same leave request.
    /// </summary>
    [MaxLength(100)]
    [Display(Name = "مرجع المصدر")]
    public string? SourceReference { get; set; }

    [Display(Name = "مطبّق في التشغيل")]
    public Guid? AppliedInRunId { get; set; }

    /// <summary>
    /// PLAN-57 (§5): the attendance period a difference row belongs to (an attendance summary's version). Null on every other row
    /// (unpaid leave, a row of before this plan).
    /// </summary>
    [Display(Name = "بداية الفترة الأصلية")]
    public DateOnly? OriginalPeriodStart { get; set; }

    [Display(Name = "نهاية الفترة الأصلية")]
    public DateOnly? OriginalPeriodEnd { get; set; }

    /// <summary>
    /// PLAN-57 (§4 rule 4): the difference arrived after its period's run was calculated. It is applied by that run's recalculation
    /// if one comes before the run is frozen, otherwise by the first later regular run, with an item that names the original period.
    /// </summary>
    [Display(Name = "محمول إلى دورة لاحقة")]
    public bool CarryForward { get; set; }

    /// <summary>PLAN-57: the summary version this row is the difference of.</summary>
    public int? SourceVersion { get; set; }

    /// <summary>The key of an attendance difference row: one row per summary version (the unique index on <see cref="SourceReference"/>).</summary>
    public static string AttendanceReference(Guid summaryId, int version) => $"ATT:{summaryId:D}:v{version}";

    /// <summary>A row payroll wrote from an attendance summary version (PLAN-57).</summary>
    public bool IsAttendanceDifference => OriginalPeriodStart.HasValue && OriginalPeriodEnd.HasValue;
}
