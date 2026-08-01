using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;
using Payroll.Domain.Services.Engine.Rules;

namespace HRManagement.UnitTests;

public class PayrollEnginePipelineTests
{
    private static PayrollCalculationContext CreateContext(
        decimal baseSalary = 10000m,
        int workingDays = 22,
        int employeeDays = 22,
        PayrollRunType runType = PayrollRunType.Regular,
        IEnumerable<AllowanceRecord>? allowances = null,
        IEnumerable<DeductionRecord>? deductions = null,
        IEnumerable<PayrollAdjustment>? adjustments = null,
        IEnumerable<AbsenceDeduction>? absences = null,
        decimal? correctionGross = null,
        decimal? correctionNet = null)
    {
        var config = new PayrollConfiguration
        {
            Code = "TEST",
            ProrationDaysBasis = ProrationDaysBasis.WorkingDays,
            TaxBrackets = PayrollEngine.DefaultBrackets.Select((b, i) => new TaxBracket
            {
                UpperLimit = b.UpperLimit,
                RatePercent = b.Rate * 100m,
                SortOrder = i
            }).ToList()
        };

        return new PayrollCalculationContext
        {
            PayrollRunId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            WorkingDaysInPeriod = workingDays,
            EmployeeWorkingDays = employeeDays,
            MonthlyBaseSalary = baseSalary,
            Allowances = allowances?.ToList() ?? [],
            Deductions = deductions?.ToList() ?? [],
            StatutoryDeductionTypes = [],
            Adjustments = adjustments?.ToList() ?? [],
            AbsenceDeductions = absences?.ToList() ?? [],
            Configuration = config,
            ExtraHolidays = new HashSet<DateOnly>(),
            RunType = runType,
            CorrectionDeltaGross = correctionGross,
            CorrectionDeltaNet = correctionNet
        };
    }

    [Fact]
    public void Pipeline_BaseSalaryOnly_ComputesNet()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var result = engine.Calculate(CreateContext(baseSalary: 4000m));
        Assert.Equal(4000m, result.GrossPay);
        Assert.Equal(0m, result.TotalDeductions);
        Assert.Equal(4000m, result.NetPay);
        Assert.DoesNotContain(result.Components, c => c.ComponentType == SalaryComponentType.Tax);
    }

    [Fact]
    public void Pipeline_AppliesTaxAboveThreshold()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var result = engine.Calculate(CreateContext(baseSalary: 20000m));
        Assert.True(result.TotalDeductions > 0);
        Assert.Contains(result.Components, c => c.ComponentType == SalaryComponentType.Tax);
        Assert.True(result.NetPay < result.GrossPay);
    }

    [Fact]
    public void Proration_MidMonthHire_ReducesBase()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var full = engine.Calculate(CreateContext(baseSalary: 22000m, employeeDays: 22)).GrossPay;
        var partial = engine.Calculate(CreateContext(baseSalary: 22000m, employeeDays: 11)).GrossPay;
        Assert.Equal(full / 2m, partial, 1);
    }

    [Fact]
    public void AbsenceRule_DeductsUnpaidDays()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var absences = new[] { new AbsenceDeduction { AbsentDays = 2, Amount = 0, Reason = "غياب" } };
        var without = engine.Calculate(CreateContext(baseSalary: 22000m));
        var withAbsence = engine.Calculate(CreateContext(baseSalary: 22000m, absences: absences));
        Assert.True(withAbsence.TotalDeductions > without.TotalDeductions);
    }

    [Fact]
    public void Rounding_NeverNegativeNet()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var deductions = new[] { new DeductionRecord { DeductionType = "Loan", Amount = 50000m } };
        var result = engine.Calculate(CreateContext(baseSalary: 1000m, deductions: deductions));
        Assert.Equal(0m, result.NetPay);
    }

    [Fact]
    public void ZeroBase_WithAdjustmentOnly()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var adjustments = new[] { new PayrollAdjustment { Amount = 500m, AdjustmentReason = "bonus" } };
        var result = engine.Calculate(CreateContext(baseSalary: 0m, adjustments: adjustments));
        Assert.Equal(0m, result.GrossPay);
        Assert.Equal(500m, result.NetPay);
    }

    [Fact]
    public void CorrectionRun_UsesDeltaNet()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var result = engine.Calculate(CreateContext(
            runType: PayrollRunType.Correction,
            correctionGross: 1000m,
            correctionNet: 800m));
        Assert.Equal(800m, result.NetPay);
    }

    [Fact]
    public void PayrollRun_Lifecycle_Transitions()
    {
        var run = new PayrollRun
        {
            PeriodStart = DateTime.UtcNow,
            PeriodEnd = DateTime.UtcNow,
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Draft
        };
        run.SalaryLines.Add(new SalaryLine { GrossPay = 1000, NetPay = 900, TotalDeductions = 100 });

        run.MarkCalculated(900);
        Assert.Equal(PayrollRunStatus.Calculated, run.Status);
        run.MarkValidated(900);
        Assert.True(run.ValidatedAtCalculate);
        Assert.True(run.ResultsFrozen);

        run.SubmitForReview();
        Assert.Equal(PayrollRunStatus.UnderReview, run.Status);

        run.Approve(Guid.NewGuid());
        Assert.Equal(PayrollRunStatus.Approved, run.Status);

        run.Post(900);
        Assert.Equal(PayrollRunStatus.Posted, run.Status);

        run.Reverse();
        Assert.Equal(PayrollRunStatus.Reversed, run.Status);
    }

    [Fact]
    public void TaxRule_MultiBracket_AppliesMarginalRates()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var result = engine.Calculate(CreateContext(baseSalary: 50000m));
        Assert.True(result.TotalDeductions > 0);
        Assert.Contains(result.Components, c => c.ComponentType == SalaryComponentType.Tax);
    }

    [Fact]
    public void RoundingRule_RoundsNetToTwoDecimals()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var allowances = new[] { new AllowanceRecord { EmployeeId = Guid.NewGuid(), AllowanceTypeId = Guid.NewGuid(), Amount = 123.456m, EffectiveFrom = DateTime.UtcNow } };
        var result = engine.Calculate(CreateContext(baseSalary: 5000m, allowances: allowances));
        Assert.Equal(Math.Round(result.NetPay, 2), result.NetPay);
    }

    [Fact]
    public void AbsenceRule_UnpaidLeave_ProratesDeduction()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var absences = new[] { new AbsenceDeduction { AbsentDays = 5, Amount = 0, Reason = "UnpaidLeave" } };
        var full = engine.Calculate(CreateContext(baseSalary: 22000m, employeeDays: 22));
        var withAbsence = engine.Calculate(CreateContext(baseSalary: 22000m, employeeDays: 22, absences: absences));
        Assert.True(withAbsence.TotalDeductions > full.TotalDeductions);
        Assert.True(withAbsence.GrossPay <= full.GrossPay);
    }

    [Fact]
    public void Termination_EOS_NeverNegative()
    {
        var settlement = EndOfServiceCalculation.CreateSettlement(
            Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2020, 1, 1), new DateTime(2025, 1, 1),
            10000, 15, 999999);
        Assert.Equal(0m, settlement.NetSettlement);
    }
}

public class PayrollPeriodRulesExtendedTests
{
    [Fact]
    public void EmployeeWorkingDays_MidMonthStart()
    {
        var days = EmployeePayProfileRules.CountEmployeeWorkingDays(
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            new DateTime(2026, 5, 15),
            null);
        Assert.True(days > 0);
        Assert.True(days < PayrollPeriodRules.CountWorkingDays(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)));
    }
}
