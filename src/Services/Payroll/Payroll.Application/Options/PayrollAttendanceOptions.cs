using System.ComponentModel.DataAnnotations;

namespace Payroll.Application.Options;

/// <summary>PLAN-57 (D-LVA-10): what a regular run's calculation does about employees whose attendance is not closed for its period.</summary>
public enum PayrollAttendanceGateMode
{
    /// <summary>The calculation goes on, and an open audit record lists who is missing (the default in every environment).</summary>
    [Display(Name = "تحذير")]
    Warn = 0,
    /// <summary>The start of the calculation is refused with the count; the run stays as it was.</summary>
    [Display(Name = "منع")]
    Block = 1,
    /// <summary>No check.</summary>
    [Display(Name = "معطّلة")]
    Off = 2
}

/// <summary>Section <c>Payroll:Attendance</c>.</summary>
public sealed class PayrollAttendanceOptions
{
    public const string SectionName = "Payroll:Attendance";

    public PayrollAttendanceGateMode Gate { get; set; } = PayrollAttendanceGateMode.Warn;
}
