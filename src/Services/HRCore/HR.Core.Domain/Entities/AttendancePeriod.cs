using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>
/// PLAN-57 (§1): an attendance period of one agency (a calendar month, D-LVA-11). It is created at its first close. Its
/// <see cref="Status"/> is for display; a day is locked by the employee's summary in the period (rule 9), and a period is never reopened.
/// </summary>
public class AttendancePeriod : AuditableEntity
{
    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public AttendancePeriodStatus Status { get; set; } = AttendancePeriodStatus.Open;

    /// <summary>When the last eligible employee of the agency got a summary (the period became <c>Closed</c>).</summary>
    public DateTime? ClosedAt { get; set; }

    public Guid? ClosedBy { get; set; }
}
