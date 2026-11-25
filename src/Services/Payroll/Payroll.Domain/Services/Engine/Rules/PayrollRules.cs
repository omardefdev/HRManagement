using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services.Engine;

namespace Payroll.Domain.Services.Engine.Rules;

/// <summary>GOV-002: LD-PRORATION-BASIS / LD-DAILY-DIVISOR — proration uses working days vs eligible days.</summary>
public sealed class BaseSalaryRule : IPayrollRule
{
    public int Order => 10;

    public void Apply(PayrollCalculationContext context, PayrollCalculationResult result)
    {
        var baseAmount = context.MonthlyBaseSalary;
        if (context.WorkingDaysInPeriod > 0
            && context.EmployeeWorkingDays > 0
            && context.EmployeeWorkingDays < context.WorkingDaysInPeriod)
        {
            baseAmount = context.Configuration.RoundMoney(
                context.MonthlyBaseSalary * context.EmployeeWorkingDays / context.WorkingDaysInPeriod);
        }

        result.Components.Add(new SalaryLineComponent
        {
            ComponentType = SalaryComponentType.BaseSalary,
            ComponentName = "الراتب الأساسي",
            Amount = baseAmount
        });
        result.GrossPay += baseAmount;
    }
}

public sealed class AllowanceRule : IPayrollRule
{
    public int Order => 20;

    public void Apply(PayrollCalculationContext context, PayrollCalculationResult result)
    {
        foreach (var a in context.Allowances)
        {
            result.Components.Add(new SalaryLineComponent
            {
                ComponentType = SalaryComponentType.Allowance,
                ComponentName = a.AllowanceType?.NameAr ?? "بدل",
                Amount = a.Amount,
                ReferenceId = a.Id
            });
            result.GrossPay += a.Amount;
        }
    }
}

public sealed class AbsenceRule : IPayrollRule
{
    public int Order => 30;

    public void Apply(PayrollCalculationContext context, PayrollCalculationResult result)
    {
        foreach (var absence in context.AbsenceDeductions)
        {
            var amount = absence.Amount;
            if (amount <= 0 && absence.AbsentDays > 0)
            {
                amount = ComputeAbsenceAmount(context, absence.AbsentDays);
            }

            if (amount <= 0) continue;

            result.Components.Add(new SalaryLineComponent
            {
                ComponentType = SalaryComponentType.Deduction,
                ComponentName = absence.Reason ?? "خصم غياب",
                Amount = -amount,
                ReferenceId = absence.Id
            });
            result.TotalDeductions += amount;
            result.ConsumedAbsenceIds.Add(absence.Id);
        }

        // "UnpaidLeave" is deliberately absent: unpaid leave is carried by AbsenceDeduction above.
        // Charging it here as well double-deducted the same leave. Legacy UnpaidLeave rows written
        // before that fix are now charged exactly once by AdHocDeductionRule.
        foreach (var d in context.Deductions.Where(x =>
                     string.Equals(x.DeductionType, "DisciplinaryDeduction", StringComparison.OrdinalIgnoreCase)))
        {
            result.Components.Add(new SalaryLineComponent
            {
                ComponentType = SalaryComponentType.Deduction,
                ComponentName = d.DeductionType,
                Amount = -d.Amount,
                ReferenceId = d.Id
            });
            result.TotalDeductions += d.Amount;
            result.ConsumedDeductionIds.Add(d.Id);
        }
    }

    internal static decimal ComputeAbsenceAmount(PayrollCalculationContext context, int absentDays)
    {
        if (context.Configuration.ProrationDaysBasis == ProrationDaysBasis.Calendar30)
        {
            var divisor = context.Configuration.DailyRateDivisor > 0 ? context.Configuration.DailyRateDivisor : 30;
            return PayrollPeriodRules.ProrateMonthlyAmount(context.MonthlyBaseSalary, divisor, absentDays);
        }

        return PayrollPeriodRules.ProrateMonthlyAmount(
            context.MonthlyBaseSalary,
            context.WorkingDaysInPeriod,
            absentDays);
    }
}

/// <summary>
/// The statutory deductions (PLAN-54, §2). For a version of fingerprint v2 the rule reads the version's own
/// <see cref="StatutoryDeductionRule"/> rows and nothing else: the employee's share of each is
/// <c>min(base, ceiling) × EmployeePercent / 100 + FixedAmount</c>, the base being gross pay or the base salary paid this period. For
/// a version older than PLAN-54 it computes exactly as before, from the general deduction types the caller loaded (the legacy path).
/// </summary>
public sealed class StatutoryDeductionEngineRule : IPayrollRule
{
    public int Order => 40;

    public void Apply(PayrollCalculationContext context, PayrollCalculationResult result)
    {
        if (context.Configuration.IsLegacyFramework)
            ApplyLegacy(context, result);
        else
            ApplyFromVersion(context, result);
    }

    private static void ApplyFromVersion(PayrollCalculationContext context, PayrollCalculationResult result)
    {
        var baseSalary = result.Components.Where(c => c.ComponentType == SalaryComponentType.BaseSalary).Sum(c => c.Amount);
        foreach (var rule in context.Configuration.StatutoryDeductions
                     .OrderBy(r => r.SortOrder)
                     .ThenBy(r => r.Code, StringComparer.Ordinal))
        {
            var amount = context.Configuration.RoundMoney(rule.CappedBase(result.GrossPay, baseSalary) * rule.EmployeePercent / 100m)
                         + rule.FixedAmount;
            if (amount <= 0) continue;

            result.Components.Add(new SalaryLineComponent
            {
                ComponentType = SalaryComponentType.Deduction,
                ComponentName = rule.NameAr,
                Amount = -amount,
                ReferenceId = rule.Id
            });
            result.TotalDeductions += amount;
        }
    }

    /// <summary>The path of a version older than PLAN-54, unchanged: the general deduction types marked statutory, on gross pay.</summary>
    private static void ApplyLegacy(PayrollCalculationContext context, PayrollCalculationResult result)
    {
        foreach (var dt in context.StatutoryDeductionTypes.Where(t => t.IsStatutory))
        {
            decimal amt = 0;
            if (dt.Percentage.HasValue)
                amt += context.Configuration.RoundMoney(result.GrossPay * dt.Percentage.Value / 100m);
            if (dt.FixedAmount.HasValue)
                amt += dt.FixedAmount.Value;

            if (amt <= 0) continue;

            result.Components.Add(new SalaryLineComponent
            {
                ComponentType = SalaryComponentType.Deduction,
                ComponentName = dt.NameAr,
                Amount = -amt,
                ReferenceId = dt.Id
            });
            result.TotalDeductions += amt;
        }
    }
}

public sealed class AdHocDeductionRule : IPayrollRule
{
    public int Order => 50;

    public void Apply(PayrollCalculationContext context, PayrollCalculationResult result)
    {
        // DisciplinaryDeduction is charged by AbsenceRule; advance recovery by AdvanceRecoveryRule.
        // Everything else — including legacy "UnpaidLeave" rows — is charged here, exactly once.
        var skipTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DisciplinaryDeduction", "AdvanceRecovery", "AdvanceFinalRecovery"
        };

        foreach (var d in context.Deductions.Where(x => !skipTypes.Contains(x.DeductionType)))
        {
            result.Components.Add(new SalaryLineComponent
            {
                ComponentType = SalaryComponentType.Deduction,
                ComponentName = d.DeductionType,
                Amount = -d.Amount,
                ReferenceId = d.Id
            });
            result.TotalDeductions += d.Amount;
            result.ConsumedDeductionIds.Add(d.Id);
        }
    }
}

/// <summary>
/// Charges advance repayment instalments that fall due in this period.
/// <para>
/// Advance instalments used to be excluded by type from every rule, yet persistence still marked
/// them applied — so an employee's whole repayment schedule was consumed by the first run without
/// a single riyal being recovered. Recovery now happens here, once per instalment, and only for
/// instalments whose due date has arrived.
/// </para>
/// </summary>
public sealed class AdvanceRecoveryRule : IPayrollRule
{
    public int Order => 55;

    private static readonly HashSet<string> RecoveryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AdvanceRecovery", "AdvanceFinalRecovery"
    };

    public void Apply(PayrollCalculationContext context, PayrollCalculationResult result)
    {
        foreach (var d in context.Deductions.Where(x => RecoveryTypes.Contains(x.DeductionType)))
        {
            // A null due date means "recover immediately" (final recovery on separation, or a row
            // written before instalments carried a schedule).
            if (d.DueDate.HasValue && d.DueDate.Value > context.PeriodEnd)
                continue;

            if (d.Amount <= 0)
                continue;

            result.Components.Add(new SalaryLineComponent
            {
                ComponentType = SalaryComponentType.Deduction,
                ComponentName = d.DeductionType,
                Amount = -d.Amount,
                ReferenceId = d.Id
            });
            result.TotalDeductions += d.Amount;
            result.ConsumedDeductionIds.Add(d.Id);
        }
    }
}

public sealed class AdjustmentRule : IPayrollRule
{
    public int Order => 60;

    public void Apply(PayrollCalculationContext context, PayrollCalculationResult result)
    {
        foreach (var adj in context.Adjustments)
        {
            // Zero-amount rows are lifecycle markers (transfer recorded, leave approved). They
            // produce no component but are still consumed by this run — leaving them unconsumed
            // would reload them into every future run forever.
            result.ConsumedAdjustmentIds.Add(adj.Id);

            if (adj.Amount == 0)
                continue;

            result.Components.Add(new SalaryLineComponent
            {
                ComponentType = SalaryComponentType.Adjustment,
                ComponentName = adj.AdjustmentReason ?? "تسوية",
                Amount = adj.Amount,
                ReferenceId = adj.Id
            });
        }
    }
}

/// <summary>GOV-002: LD-TAX-BRACKETS / LD-TAXABLE-BASE / LD-TAX-EXEMPTION — tax from PayrollConfiguration.</summary>
public sealed class TaxRule : IPayrollRule
{
    public int Order => 70;

    public void Apply(PayrollCalculationContext context, PayrollCalculationResult result)
    {
        var brackets = context.Configuration.TaxBrackets
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.UpperLimit)
            .Select(b => new PayrollEngine.TaxBracketEntry(b.UpperLimit, b.RatePercent / 100m))
            .ToList();

        // The built-in brackets are a fallback of the legacy path only. A v2 version without brackets is never published, and if one
        // were calculated it would be taxed by no bracket rather than by brackets nobody approved.
        if (brackets.Count == 0)
        {
            if (!context.Configuration.IsLegacyFramework)
                return;
            brackets = PayrollEngine.DefaultBrackets.ToList();
        }

        var taxableBase = result.GrossPay;
        if (string.Equals(context.Configuration.TaxableBaseMode, "NetOfStatutory", StringComparison.OrdinalIgnoreCase))
            taxableBase -= result.TotalDeductions;

        taxableBase -= context.Configuration.TaxExemptionAmount;
        if (taxableBase <= 0) return;

        var tax = PayrollEngine.CalculateTax(taxableBase, brackets, context.Configuration);
        if (tax <= 0) return;

        result.Components.Add(new SalaryLineComponent
        {
            ComponentType = SalaryComponentType.Tax,
            ComponentName = "ضريبة الدخل",
            Amount = -tax
        });
        result.TotalDeductions += tax;
    }
}

public sealed class RoundingRule : IPayrollRule
{
    // GOV-002: LD-ROUNDING
    public int Order => 100;

    public void Apply(PayrollCalculationContext context, PayrollCalculationResult result)
    {
        // PLAN-56 (G-PAY-03): the line carries its adjustments beside its gross and its deductions, and the net is PayrollNet's.
        result.TotalAdjustments = context.Adjustments.Sum(a => a.Amount);
        result.GrossPay = context.Configuration.RoundMoney(result.GrossPay);
        result.TotalDeductions = context.Configuration.RoundMoney(result.TotalDeductions);

        result.NetPay = PayrollNet.Net(result.GrossPay, result.TotalDeductions, result.TotalAdjustments, context.Configuration);

        result.Line.GrossPay = result.GrossPay;
        result.Line.TotalDeductions = result.TotalDeductions;
        result.Line.TotalAdjustments = result.TotalAdjustments;
        result.Line.NetPay = result.NetPay;
    }
}

public sealed class PayrollEnginePipeline(IEnumerable<IPayrollRule> rules) : IPayrollEngine
{
    private readonly IReadOnlyList<IPayrollRule> _rules = rules.OrderBy(r => r.Order).ToList();

    /// <summary>The rules a correction line passes: its adjustments, and the one net of them.</summary>
    public static bool AppliesToACorrection(IPayrollRule rule) => rule is AdjustmentRule or RoundingRule;

    public PayrollCalculationResult Calculate(PayrollCalculationContext context)
    {
        var result = new PayrollCalculationResult
        {
            Line = new SalaryLine
            {
                PayrollRunId = context.PayrollRunId,
                EmployeeId = context.EmployeeId
            }
        };

        foreach (var rule in _rules)
        {
            // PLAN-56 (G-PAY-03, D-PAY-17): a correction line is the adjustments aimed at its source run and nothing else. It has
            // no base salary, no allowance, no statutory deduction and no tax: the difference is outside the tax and insurance base,
            // in a correction as in a regular run, and it is counted once (it used to be written into the base as well, and taxed).
            if (context.RunType == PayrollRunType.Correction && !AppliesToACorrection(rule))
                continue;
            rule.Apply(context, result);
        }

        return result;
    }
}
