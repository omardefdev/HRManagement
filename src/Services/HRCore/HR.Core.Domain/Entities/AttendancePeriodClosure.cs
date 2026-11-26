using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>PLAN-57 (§1): one act of closing: who closed which unit's tree (null: the whole agency), and how many employees it covered.</summary>
public class AttendancePeriodClosure : AuditableEntity
{
    public Guid AttendancePeriodId { get; set; }

    /// <summary>The unit whose tree was closed; null when the whole agency was.</summary>
    public Guid? OrgUnitId { get; set; }

    public Guid? ClosedBy { get; set; }

    public DateTime ClosedAt { get; set; }

    /// <summary>The employees this act gave a summary.</summary>
    public int EmployeeCount { get; set; }

    /// <summary>The employees in its scope that an earlier act had closed already.</summary>
    public int SkippedAlreadyClosed { get; set; }

    public virtual AttendancePeriod? Period { get; set; }
}
