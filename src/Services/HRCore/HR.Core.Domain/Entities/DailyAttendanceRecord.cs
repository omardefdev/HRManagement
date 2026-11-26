using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class DailyAttendanceRecord : AuditableEntity
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

    /// <summary>PLAN-57 (§2): what wrote the day. Set by <c>AttendanceDayWriter</c> only.</summary>
    [Display(Name = "المصدر")]
    public AttendanceRecordSource Source { get; set; } = AttendanceRecordSource.Manual;

    /// <summary>PLAN-57 (§2): <c>LeaveRequestId=&lt;id&gt;</c> for a leave-made day, <c>AttendanceCorrection:&lt;id&gt;</c> for a corrected one.</summary>
    [MaxLength(100)]
    public string? SourceReference { get; set; }

    /// <summary>
    /// PLAN-57 (§2, rule 6): the status the day had before an approved leave took it, so that a cancelled leave restores it. Null when
    /// the leave created the record (a cancelled leave then removes it).
    /// </summary>
    public DailyAttendanceStatus? ReplacedStatus { get; set; }

    public virtual Employee? Employee { get; set; }
}
