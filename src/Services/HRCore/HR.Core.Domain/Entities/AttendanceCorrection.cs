using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>
/// PLAN-57 (§3): a change of a locked attendance day. A person asks for it with a reason and another, central, person decides it;
/// or a leave decision makes it and approves it at once (D-LVA-12), with no requester and no decider. One request waits per
/// employee and day (a filtered unique index).
/// </summary>
public class AttendanceCorrection : AuditableEntity
{
    public const int ReasonMinLength = 15;
    public const int ReasonMaxLength = 1000;

    public Guid AttendancePeriodId { get; set; }

    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "اليوم")]
    public DateOnly AttendanceDate { get; set; }

    /// <summary>The day's status when the correction was asked for; null when the day had no record.</summary>
    [Display(Name = "الحالة قبل التصحيح")]
    public DailyAttendanceStatus? OldStatus { get; set; }

    /// <summary>
    /// The status the day takes. Null only on a system correction that undoes a leave on a day that had no record before the leave:
    /// the record is removed.
    /// </summary>
    [Display(Name = "الحالة بعد التصحيح")]
    public DailyAttendanceStatus? NewStatus { get; set; }

    [Display(Name = "السبب")]
    [MaxLength(ReasonMaxLength)]
    public string Reason { get; set; } = string.Empty;

    public AttendanceCorrectionSource Source { get; set; } = AttendanceCorrectionSource.Manual;

    /// <summary>For a leave event: <c>LeaveRequestId=&lt;id&gt;:Approved</c> or <c>…:Cancelled</c>, so the same event makes one correction.</summary>
    [MaxLength(100)]
    public string? SourceReference { get; set; }

    [Display(Name = "حالة الطلب")]
    public AttendanceCorrectionStatus Status { get; set; } = AttendanceCorrectionStatus.Pending;

    /// <summary>The requester's subject; null on a system correction.</summary>
    public Guid? RequestedBy { get; set; }

    /// <summary>The requester's name as he was signed in, for the decider's page; null on a system correction.</summary>
    [MaxLength(200)]
    public string? RequestedByName { get; set; }

    public DateTime RequestedAt { get; set; }

    /// <summary>The decider's subject; null while pending and on a system correction.</summary>
    public Guid? DecidedBy { get; set; }

    [MaxLength(200)]
    public string? DecidedByName { get; set; }

    public DateTime? DecidedAt { get; set; }

    [Display(Name = "ملاحظة القرار")]
    [MaxLength(1000)]
    public string? DecisionNote { get; set; }

    /// <summary>The summary's version this correction produced, once approved.</summary>
    public int? ResultingVersion { get; set; }

    public virtual AttendancePeriod? Period { get; set; }

    public virtual Employee? Employee { get; set; }
}
