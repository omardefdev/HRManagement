using Payroll.Domain.Entities;

namespace Payroll.Domain.Services;

/// <summary>PAY-003: independent reconciliation of run totals vs lines/components.</summary>
public static class PayrollRunReconciliation
{
    public static (bool Ok, string? Error) ValidateLineIntegrity(
        IReadOnlyList<SalaryLine> lines,
        IReadOnlyDictionary<Guid, List<SalaryLineComponent>> componentsByLine,
        int? expectedEmployeeCount = null,
        decimal roundingTolerance = 0.01m)
    {
        if (expectedEmployeeCount.HasValue && lines.Count != expectedEmployeeCount.Value)
            return (false, $"عدد الأسطر ({lines.Count}) لا يطابق لقطة المدخلات ({expectedEmployeeCount}).");

        foreach (var line in lines)
        {
            if (!componentsByLine.TryGetValue(line.Id, out var comps) || comps.Count == 0)
                return (false, $"السطر {line.EmployeeId:N} بلا مكوّنات.");

            var impliedNet = line.GrossPay - line.TotalDeductions;
            if (Math.Abs(impliedNet - line.NetPay) > roundingTolerance
                && Math.Abs(Math.Max(0, impliedNet) - line.NetPay) > roundingTolerance)
                return (false, $"عدم تطابق صافي السطر للموظف {line.EmployeeId:N}.");

            var componentNet = comps.Sum(c => c.Amount);
            if (Math.Abs(componentNet - line.NetPay) > roundingTolerance
                && Math.Abs(componentNet - (line.GrossPay - line.TotalDeductions)) > roundingTolerance)
                return (false, $"مجموع مكوّنات السطر لا يطابق الإجماليات للموظف {line.EmployeeId:N}.");
        }

        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateTotals(
        PayrollRun run,
        IReadOnlyList<SalaryLine> lines,
        IReadOnlyDictionary<Guid, List<SalaryLineComponent>> componentsByLine,
        int? expectedEmployeeCount = null,
        decimal roundingTolerance = 0.01m)
    {
        var integrity = ValidateLineIntegrity(lines, componentsByLine, expectedEmployeeCount, roundingTolerance);
        if (!integrity.Ok) return integrity;

        var sumNet = lines.Sum(l => l.NetPay);
        if (Math.Abs(sumNet - run.TotalNetPay) > roundingTolerance)
            return (false, $"مجموع الصافي ({sumNet}) لا يطابق TotalNetPay ({run.TotalNetPay}).");

        return (true, null);
    }

    public static decimal SumNet(IEnumerable<SalaryLine> lines) => lines.Sum(l => l.NetPay);
}
