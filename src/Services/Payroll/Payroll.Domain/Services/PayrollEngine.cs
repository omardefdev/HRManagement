using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services.Engine;
using Payroll.Domain.Services.Engine.Rules;

namespace Payroll.Domain.Services;

public static class PayrollEngine
{
    public record TaxBracketEntry(decimal UpperLimit, decimal Rate);

    public static readonly IReadOnlyList<TaxBracketEntry> DefaultBrackets = new[]
    {
        new TaxBracketEntry(5_000m,   0.00m),
        new TaxBracketEntry(15_000m,  0.05m),
        new TaxBracketEntry(30_000m,  0.10m),
        new TaxBracketEntry(50_000m,  0.15m),
        new TaxBracketEntry(decimal.MaxValue, 0.20m),
    };

    private static readonly IPayrollEngine DefaultPipeline = new PayrollEnginePipeline(new IPayrollRule[]
    {
        new Engine.Rules.BaseSalaryRule(),
        new Engine.Rules.AllowanceRule(),
        new Engine.Rules.AbsenceRule(),
        new Engine.Rules.StatutoryDeductionRule(),
        new Engine.Rules.AdHocDeductionRule(),
        new Engine.Rules.AdvanceRecoveryRule(),
        new Engine.Rules.AdjustmentRule(),
        new Engine.Rules.TaxRule(),
        new Engine.Rules.RoundingRule(),
    });

    public static IPayrollEngine CreateDefaultPipeline() => DefaultPipeline;

    public static decimal CalculateTax(
        decimal grossTaxable,
        IReadOnlyList<TaxBracketEntry>? brackets = null,
        PayrollConfiguration? config = null)
    {
        brackets ??= DefaultBrackets;
        if (grossTaxable <= 0 || brackets.Count == 0) return 0m;

        decimal tax = 0m;
        decimal previousLimit = 0m;

        foreach (var bracket in brackets.OrderBy(b => b.UpperLimit))
        {
            if (grossTaxable <= previousLimit) break;

            var taxableInBracket = Math.Min(grossTaxable, bracket.UpperLimit) - previousLimit;
            if (taxableInBracket > 0)
                tax += taxableInBracket * bracket.Rate;

            previousLimit = bracket.UpperLimit;
        }

        return config?.RoundMoney(tax) ?? decimal.Round(tax, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal CalculateEndOfServiceReal(
        DateTime hireDate,
        DateTime separationDate,
        decimal lastBaseSalary,
        PayrollConfiguration? config = null)
    {
        var serviceYears = EndOfServiceCalculation.CalculateServiceYears(hireDate, separationDate);
        return EndOfServiceCalculation.CalculateGratuity(lastBaseSalary, serviceYears, config);
    }

    public static (SalaryLine Line, List<SalaryLineComponent> Components) BuildSalaryLines(
        Guid payrollRunId,
        Guid employeeId,
        decimal baseSalary,
        IEnumerable<AllowanceRecord> allowances,
        IEnumerable<DeductionRecord> deductions,
        IEnumerable<DeductionType> deductionTypes,
        IEnumerable<PayrollAdjustment> adjustments,
        IReadOnlyList<TaxBracketEntry>? taxBrackets = null)
    {
        var config = new PayrollConfiguration();
        if (taxBrackets is { Count: > 0 })
        {
            config.TaxBrackets = taxBrackets.Select((b, i) => new TaxBracket
            {
                UpperLimit = b.UpperLimit,
                RatePercent = b.Rate * 100m,
                SortOrder = i
            }).ToList();
        }
        else
        {
            config.TaxBrackets = DefaultBrackets.Select((b, i) => new TaxBracket
            {
                UpperLimit = b.UpperLimit,
                RatePercent = b.Rate * 100m,
                SortOrder = i
            }).ToList();
        }

        var periodStart = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodEnd = periodStart.AddDays(30);
        var workingDays = PayrollPeriodRules.CountWorkingDays(periodStart, periodEnd);

        var context = new PayrollCalculationContext
        {
            PayrollRunId = payrollRunId,
            EmployeeId = employeeId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            WorkingDaysInPeriod = workingDays,
            EmployeeWorkingDays = workingDays,
            MonthlyBaseSalary = baseSalary,
            Allowances = allowances.ToList(),
            Deductions = deductions.ToList(),
            StatutoryDeductionTypes = deductionTypes.ToList(),
            Adjustments = adjustments.ToList(),
            AbsenceDeductions = Array.Empty<AbsenceDeduction>(),
            Configuration = config,
            ExtraHolidays = new HashSet<DateOnly>()
        };

        var result = DefaultPipeline.Calculate(context);
        return (result.Line, result.Components);
    }
}
