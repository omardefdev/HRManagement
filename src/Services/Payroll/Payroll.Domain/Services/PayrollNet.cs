using Payroll.Domain.Entities;

namespace Payroll.Domain.Services;

/// <summary>
/// PLAN-56 (G-PAY-03): the one definition of a salary line's net pay. The engine, the validation, the posting's check and every
/// builder of a line use it; nothing else in Payroll computes a net from a gross and deductions (an architecture test keeps it so).
/// <para>
/// <c>Unclipped = gross − deductions + adjustments</c>, and <c>Net = round(max(0, Unclipped))</c> with the rounding of the run's
/// pinned legal framework. A negative <c>deductions</c> is legal: an attendance recovery larger than the month's deductions gives the
/// employee money back (PLAN-57 §4, rule 5). A negative <c>Unclipped</c> is not paid and not lost: a regular run clips it (carrying
/// the excess is wave 2, PAY-E), and a correction run turns it into a recovery deduction of the next cycle.
/// </para>
/// A reader never recomputes a stored net with this: a posted line shows the net it was posted with.
/// </summary>
public static class PayrollNet
{
    /// <summary>What the line's parts add up to before the floor at zero. It equals the sum of the line's components.</summary>
    public static decimal Unclipped(decimal grossPay, decimal totalDeductions, decimal totalAdjustments) =>
        grossPay - totalDeductions + totalAdjustments;

    public static decimal Unclipped(SalaryLine line) =>
        Unclipped(line.GrossPay, line.TotalDeductions, line.TotalAdjustments);

    /// <summary>The net of the parts, floored at zero and rounded by <paramref name="round"/> (the framework's money rounding).</summary>
    public static decimal Net(decimal grossPay, decimal totalDeductions, decimal totalAdjustments, Func<decimal, decimal> round) =>
        round(Math.Max(0m, Unclipped(grossPay, totalDeductions, totalAdjustments)));

    /// <summary>The net of the parts under a legal framework version's rounding.</summary>
    public static decimal Net(decimal grossPay, decimal totalDeductions, decimal totalAdjustments, PayrollConfiguration framework) =>
        Net(grossPay, totalDeductions, totalAdjustments, framework.RoundMoney);

    /// <summary>The net a line's stored parts imply, under the rounding given (none: the parts are taken as already rounded).</summary>
    public static decimal Net(SalaryLine line, Func<decimal, decimal>? round = null) =>
        Net(line.GrossPay, line.TotalDeductions, line.TotalAdjustments, round ?? NoRounding);

    /// <summary>
    /// The amount a correction run has to take back in the next cycle: how far the line's parts fall below zero. Zero when the
    /// line pays something or nothing.
    /// </summary>
    public static decimal Shortfall(SalaryLine line) => Math.Max(0m, -Unclipped(line));

    /// <summary>The rounding of lines built without a framework version (seed data, the oldest tests): two places, away from zero.</summary>
    public static decimal LegacyRound(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal NoRounding(decimal value) => value;
}
