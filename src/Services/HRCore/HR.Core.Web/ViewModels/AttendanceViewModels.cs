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

/// <summary>PLAN-57: the corrections the caller may see (his scope): those waiting for a decision, or his own requests.</summary>
public sealed class AttendanceCorrectionsIndexViewModel
{
    public IReadOnlyList<HR.Core.Domain.Entities.AttendanceCorrection> Items { get; init; } = [];

    public AttendanceCorrectionStatus? Status { get; init; }

    public bool Mine { get; init; }

    /// <summary>The caller is central and may decide a request that is not his own.</summary>
    public bool CanDecide { get; init; }

    public Guid? CurrentUserId { get; init; }
}

/// <summary>
/// PLAN-57: a request to correct a locked day. The employee and the day come from the day the request starts from and are posted
/// back as they are; the requester chooses the status and writes the reason.
/// </summary>
public sealed class AttendanceCorrectionCreateViewModel
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [System.ComponentModel.DataAnnotations.Display(Name = "اليوم")]
    public DateOnly AttendanceDate { get; set; }

    [System.ComponentModel.DataAnnotations.Display(Name = "الحالة المطلوبة")]
    public DailyAttendanceStatus NewStatus { get; set; }

    [System.ComponentModel.DataAnnotations.Display(Name = "سبب التصحيح")]
    public string? Reason { get; set; }

    // What the form shows and does not bind.
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public string? EmployeeName { get; set; }

    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public DailyAttendanceStatus? CurrentStatus { get; set; }

    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public bool LeaveMade { get; set; }

    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public bool Locked { get; set; }
}

public sealed class AttendanceCorrectionDetailsViewModel
{
    public required HR.Core.Domain.Entities.AttendanceCorrection Correction { get; init; }

    /// <summary>The caller may decide this request: central, the request waits, and it is not his own.</summary>
    public bool CanDecide { get; init; }

    public bool IsOwnRequest { get; init; }
}
