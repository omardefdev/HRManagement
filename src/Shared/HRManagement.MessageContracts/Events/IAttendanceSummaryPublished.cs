namespace HRManagement.MessageContracts.Events;

/// <summary>
/// PLAN-57 (§4): one version of one employee's attendance summary for one period, published by HR.Core through its outbox in the
/// save that wrote the version (the close writes version 1; each approved correction writes the next).
/// <para>
/// <b>The values are absolute for the version.</b> A consumer keeps the latest version it has seen per <see cref="SummaryId"/> and
/// derives what it needs (payroll records the difference of <see cref="AbsentDays"/> against what it recorded before); the same
/// version is never applied twice, and a version older than the latest recorded is ignored without loss. <see cref="AbsentDays"/> is
/// counted on working days only, and includes the unrecorded working days when the agency's setting counts them as absence.
/// </para>
/// </summary>
public interface IAttendanceSummaryPublished
{
    /// <summary>Stable for (period, employee): the consumer's identity key.</summary>
    Guid SummaryId { get; }

    /// <summary>1 at the close; +1 for each approved correction.</summary>
    int Version { get; }

    Guid AttendancePeriodId { get; }
    DateOnly PeriodStart { get; }
    DateOnly PeriodEnd { get; }
    Guid EmployeeId { get; }

    int WorkingDays { get; }
    int PresentDays { get; }
    int AbsentDays { get; }
    int OnLeaveDays { get; }
    int RemoteDays { get; }
    int UnrecordedWorkingDays { get; }

    /// <summary><c>Close</c>, <c>Correction</c> or <c>LeaveEvent</c>.</summary>
    string Reason { get; }

    /// <summary>The correction that produced the version, for tracing; null at the close.</summary>
    Guid? CorrectionId { get; }

    /// <summary>The agency. Set explicitly by the publisher; the tenant header carries the same value.</summary>
    Guid? TenantId { get; }

    DateTime OccurredAt { get; }
    string? CorrelationId { get; }
    string SourceSystem { get; }

    int SchemaVersion => 1;
}
