using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Services;

public static class PayrollCalculation
{
    public static void ApplyNetFromGrossAndDeductions(SalaryLine line)
    {
        line.NetPay = decimal.Round(Math.Max(0, line.GrossPay - line.TotalDeductions), 2, MidpointRounding.AwayFromZero);
    }

    public static decimal SumNetPay(IEnumerable<SalaryLine> lines) =>
        lines.Sum(l => l.NetPay);

    public static string? ValidatePayrollPeriod(DateTime periodStart, DateTime periodEnd)
    {
        if (periodEnd < periodStart)
            return "نهاية الفترة يجب ألا تسبق بدايتها.";
        return null;
    }

    public static decimal CalculateGross(decimal baseSalary, IEnumerable<AllowanceRecord> allowances)
    {
        var totalAllowances = allowances.Sum(a => a.Amount);
        return decimal.Round(baseSalary + totalAllowances, 2);
    }

    public static decimal CalculateDeductions(
        decimal gross,
        IEnumerable<DeductionRecord> deductions,
        IEnumerable<DeductionType> deductionTypes)
    {
        decimal total = 0;
        foreach (var d in deductions)
        {
            total += d.Amount;
        }

        foreach (var dt in deductionTypes.Where(t => t.IsStatutory))
        {
            if (dt.Percentage.HasValue)
                total += decimal.Round(gross * dt.Percentage.Value / 100m, 2);
            if (dt.FixedAmount.HasValue)
                total += dt.FixedAmount.Value;
        }

        return decimal.Round(total, 2);
    }

    public static decimal CalculateNetPay(decimal gross, decimal totalDeductions) =>
        decimal.Round(Math.Max(0, gross - totalDeductions), 2, MidpointRounding.AwayFromZero);

    public static (SalaryLine Line, List<SalaryLineComponent> Components) GenerateSalaryLine(
        Guid payrollRunId,
        Guid employeeId,
        decimal baseSalary,
        IEnumerable<AllowanceRecord> allowances,
        IEnumerable<DeductionRecord> deductions,
        IEnumerable<DeductionType> deductionTypes,
        IEnumerable<PayrollAdjustment> adjustments)
    {
        var components = new List<SalaryLineComponent>();
        var line = new SalaryLine
        {
            PayrollRunId = payrollRunId,
            EmployeeId = employeeId
        };

        components.Add(new SalaryLineComponent
        {
            ComponentType = SalaryComponentType.BaseSalary,
            ComponentName = "الراتب الأساسي",
            Amount = baseSalary
        });

        decimal totalAllowances = 0;
        foreach (var a in allowances)
        {
            totalAllowances += a.Amount;
            components.Add(new SalaryLineComponent
            {
                ComponentType = SalaryComponentType.Allowance,
                ComponentName = a.AllowanceType?.NameAr ?? "بدل",
                Amount = a.Amount,
                ReferenceId = a.Id
            });
        }

        line.GrossPay = decimal.Round(baseSalary + totalAllowances, 2);

        decimal totalDeductions = 0;
        foreach (var d in deductions)
        {
            totalDeductions += d.Amount;
            components.Add(new SalaryLineComponent
            {
                ComponentType = SalaryComponentType.Deduction,
                ComponentName = d.DeductionType,
                Amount = -d.Amount,
                ReferenceId = d.Id
            });
        }

        foreach (var dt in deductionTypes.Where(t => t.IsStatutory))
        {
            decimal amt = 0;
            if (dt.Percentage.HasValue)
                amt += decimal.Round(line.GrossPay * dt.Percentage.Value / 100m, 2);
            if (dt.FixedAmount.HasValue)
                amt += dt.FixedAmount.Value;
            if (amt > 0)
            {
                totalDeductions += amt;
                components.Add(new SalaryLineComponent
                {
                    ComponentType = SalaryComponentType.Deduction,
                    ComponentName = dt.NameAr,
                    Amount = -amt,
                    ReferenceId = dt.Id
                });
            }
        }

        decimal totalAdjustments = 0;
        foreach (var adj in adjustments)
        {
            totalAdjustments += adj.Amount;
            components.Add(new SalaryLineComponent
            {
                ComponentType = SalaryComponentType.Adjustment,
                ComponentName = adj.AdjustmentReason ?? "تسوية",
                Amount = adj.Amount,
                ReferenceId = adj.Id
            });
        }

        line.TotalDeductions = decimal.Round(totalDeductions, 2);
        line.NetPay = decimal.Round(Math.Max(0, line.GrossPay - totalDeductions + totalAdjustments), 2, MidpointRounding.AwayFromZero);

        return (line, components);
    }
}
