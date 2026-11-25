using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>
/// PLAN-57 (§1, §4): one employee's attendance in one period: a single row per (period, employee), whose <see cref="Version"/> is 1 at
/// the close and rises by one with each approved correction. Its existence locks the employee's days in the period. The counts are
/// absolute for the version and are taken on working days only (<c>AttendanceSummaryCalculator</c>). <c>Id</c> is the
/// <c>SummaryId</c> payroll keys its receipt by.
/// </summary>
public class AttendanceEmployeeSummary : AuditableEntity
{
    public Guid AttendancePeriodId { get; set; }

    public Guid EmployeeId { get; set; }

    public int Version { get; set; } = 1;

    public int WorkingDays { get; set; }

    public int PresentDays { get; set; }

    public int AbsentDays { get; set; }

    public int OnLeaveDays { get; set; }

    public int RemoteDays { get; set; }

    public int UnrecordedWorkingDays { get; set; }

    public AttendanceSummaryReason LastReason { get; set; } = AttendanceSummaryReason.Close;

    public Guid? LastCorrectionId { get; set; }

    /// <summary>When the current version was staged for payroll (the save that wrote it).</summary>
    public DateTime PublishedAt { get; set; }

    public virtual AttendancePeriod? Period { get; set; }

    public virtual Employee? Employee { get; set; }
}
