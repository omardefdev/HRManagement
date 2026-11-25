using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Attendance;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Core.Web.ViewModels;

/// <summary>PLAN-57: the attendance periods as the caller may see them (a branch: its own tree).</summary>
public sealed class AttendancePeriodsIndexViewModel
{
    public IReadOnlyList<AttendancePeriodView> Periods { get; init; } = [];

    public AttendanceUnrecordedDaysMode UnrecordedMode { get; init; }
}

/// <summary>PLAN-57: the close form, and what a refused close shows (the employees with working days that have no record).</summary>
public sealed class AttendancePeriodCloseViewModel
{
    public sealed record MissingRow(string EmployeeName, int UnrecordedWorkingDays);

    /// <summary>The month, as an HTML month input gives it (<c>yyyy-MM</c>).</summary>
    public string? Month { get; set; }

    public Guid? OrgUnitId { get; set; }

    public SelectList? OrgUnits { get; set; }

    /// <summary>A central user may close the whole agency (no unit chosen); a branch chooses a unit of its tree.</summary>
    public bool CanCloseWholeAgency { get; set; }

    public bool NoHolidayConfigured { get; set; }

    public bool NoHolidayInPeriod { get; set; }

    public AttendanceUnrecordedDaysMode UnrecordedMode { get; set; }

    public IReadOnlyList<MissingRow> Incomplete { get; set; } = [];

    public int IncompleteTotal { get; set; }
}
