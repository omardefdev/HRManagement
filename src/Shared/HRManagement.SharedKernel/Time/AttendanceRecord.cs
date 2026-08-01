using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Time;

/// <summary>HCM-002: a single attendance punch or computed daily record for an employee.</summary>
public class AttendanceRecord : BaseEntity
{
    [Required]
    public Guid EmployeeId { get; set; }

    public DateTime WorkDate { get; set; }

    public DateTime? ClockIn { get; set; }

    public DateTime? ClockOut { get; set; }

    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;

    /// <summary>Computed or manually entered total working minutes.</summary>
    public int WorkedMinutes { get; set; }

    /// <summary>Approved overtime minutes for this day.</summary>
    public int OvertimeMinutes { get; set; }

    /// <summary>FK to the shift this record was evaluated against.</summary>
    public Guid? ShiftDefinitionId { get; set; }

    /// <summary>Source device or import batch identifier.</summary>
    [MaxLength(100)]
    public string? SourceDeviceId { get; set; }

    /// <summary>Correction reason when record is manually adjusted.</summary>
    [MaxLength(500)]
    public string? CorrectionReason { get; set; }

    /// <summary>Whether this record has been locked into a payroll snapshot.</summary>
    public bool IsPayrollLocked { get; set; }

    public ShiftDefinition? ShiftDefinition { get; set; }

    public bool IsComplete => ClockIn.HasValue && ClockOut.HasValue;

    public bool CrossesMidnight =>
        ClockIn.HasValue && ClockOut.HasValue && ClockOut.Value.Date > ClockIn.Value.Date;
}

public enum AttendanceStatus
{
    Present = 0,
    Absent = 1,
    Leave = 2,
    Holiday = 3,
    Rest = 4,
    Mission = 5
}
