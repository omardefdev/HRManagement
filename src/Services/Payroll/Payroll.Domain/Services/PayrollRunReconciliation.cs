using Payroll.Domain.Entities;

namespace Payroll.Domain.Services;

/// <summary>
/// PAY-003: independent reconciliation of run totals vs lines/components.
/// <para>
/// PLAN-56 (G-PAY-03): a line is sound when its net is <see cref="PayrollNet"/> of its gross, deductions and adjustments, and its
/// components add up to the same parts before the floor at zero. <paramref name="round"/> is the money rounding of the run's pinned
/// framework version; without it the parts are taken as already rounded.
/// </para>
/// </summary>
public static class PayrollRunReconciliation
{
    /// <summary>The refusal of a posting whose lines no longer add up to what the validation froze.</summary>
    public const string ResultsChangedSinceValidation = "تغيّرت النتائج بعد التحقق؛ لا يمكن الترحيل. أعد التحقق من التشغيل أو ألغِه.";

    public static (bool Ok, string? Error) ValidateLineIntegrity(
        IReadOnlyList<SalaryLine> lines,
        IReadOnlyDictionary<Guid, List<SalaryLineComponent>> componentsByLine,
        int? expectedEmployeeCount = null,
        decimal roundingTolerance = 0.01m,
        Func<decimal, decimal>? round = null)
    {
        if (expectedEmployeeCount.HasValue && lines.Count != expectedEmployeeCount.Value)
            return (false, $"عدد الأسطر ({lines.Count}) لا يطابق لقطة المدخلات ({expectedEmployeeCount}).");

        foreach (var line in lines)
        {
            if (!componentsByLine.TryGetValue(line.Id, out var comps) || comps.Count == 0)
                return (false, $"السطر {line.EmployeeId:N} بلا مكوّنات.");

            if (Math.Abs(PayrollNet.Net(line, round) - line.NetPay) > roundingTolerance)
                return (false, $"عدم تطابق صافي السطر للموظف {line.EmployeeId:N}.");

            var componentSum = comps.Sum(c => c.Amount);
            if (Math.Abs(componentSum - PayrollNet.Unclipped(line)) > roundingTolerance)
                return (false, $"مجموع مكوّنات السطر لا يطابق الإجماليات للموظف {line.EmployeeId:N}.");
        }

        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateTotals(
        PayrollRun run,
        IReadOnlyList<SalaryLine> lines,
        IReadOnlyDictionary<Guid, List<SalaryLineComponent>> componentsByLine,
        int? expectedEmployeeCount = null,
        decimal roundingTolerance = 0.01m,
        Func<decimal, decimal>? round = null)
    {
        var integrity = ValidateLineIntegrity(lines, componentsByLine, expectedEmployeeCount, roundingTolerance, round);
        if (!integrity.Ok) return integrity;

        var sumNet = lines.Sum(l => l.NetPay);
        if (Math.Abs(sumNet - run.TotalNetPay) > roundingTolerance)
            return (false, $"مجموع الصافي ({sumNet}) لا يطابق TotalNetPay ({run.TotalNetPay}).");

        return (true, null);
    }

    public static decimal SumNet(IEnumerable<SalaryLine> lines) => lines.Sum(l => l.NetPay);
}
