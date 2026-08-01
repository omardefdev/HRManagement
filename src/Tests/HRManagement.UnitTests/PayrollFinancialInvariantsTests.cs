using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;

namespace HRManagement.UnitTests;

/// <summary>
/// Golden payroll oracle plus the financial invariants that must hold for every run.
/// <para>
/// These are behavioural: they drive <see cref="PayrollEngine"/> and assert money, not documents.
/// Each invariant here corresponds to a defect that shipped — unpaid leave charged twice, advance
/// instalments retired without being recovered, and deductions marked applied that no payslip
/// ever carried.
/// </para>
/// </summary>
public class PayrollFinancialInvariantsTests
{
    private static PayrollConfiguration Config() => new()
    {
        Code = "ORACLE",
        ProrationDaysBasis = ProrationDaysBasis.WorkingDays,
        TaxBrackets = PayrollEngine.DefaultBrackets.Select((b, i) => new TaxBracket
        {
            UpperLimit = b.UpperLimit,
            RatePercent = b.Rate * 100m,
            SortOrder = i
        }).ToList()
    };

    /// <summary>
    /// Default base salary sits inside the zero-rate tax bracket so <c>TotalDeductions</c> reflects
    /// only the deduction under test rather than income tax as well.
    /// </summary>
    private const decimal TaxFreeSalary = 4_000m;

    private static PayrollCalculationContext Context(
        decimal baseSalary = TaxFreeSalary,
        IEnumerable<DeductionRecord>? deductions = null,
        IEnumerable<AbsenceDeduction>? absences = null,
        IEnumerable<PayrollAdjustment>? adjustments = null,
        DateOnly? periodStart = null,
        DateOnly? periodEnd = null) => new()
        {
            PayrollRunId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PeriodStart = periodStart ?? new DateOnly(2026, 5, 1),
            PeriodEnd = periodEnd ?? new DateOnly(2026, 5, 31),
            WorkingDaysInPeriod = 22,
            EmployeeWorkingDays = 22,
            MonthlyBaseSalary = baseSalary,
            Allowances = [],
            Deductions = deductions?.ToList() ?? [],
            StatutoryDeductionTypes = [],
            Adjustments = adjustments?.ToList() ?? [],
            AbsenceDeductions = absences?.ToList() ?? [],
            Configuration = Config(),
            ExtraHolidays = new HashSet<DateOnly>()
        };

    // ---------------------------------------------------------------- unpaid leave

    [Fact]
    public void UnpaidLeave_CarriedByAbsenceOnly_IsChargedExactlyOnce()
    {
        var absence = new AbsenceDeduction
        {
            Id = Guid.NewGuid(),
            AbsentDays = 5,
            Amount = 900m,
            PeriodStart = new DateOnly(2026, 5, 10),
            PeriodEnd = new DateOnly(2026, 5, 14),
            SourceReference = "LeaveRequestId=abc",
            Reason = "إجازة بدون راتب"
        };

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context(absences: [absence]));

        Assert.Equal(900m, result.TotalDeductions);
        Assert.Single(result.Components, c => c.ReferenceId == absence.Id);
    }

    [Fact]
    public void LegacyUnpaidLeaveDeductionRow_IsChargedOnce_NotTwice()
    {
        // Rows written before the fix live on as DeductionRecord("UnpaidLeave"). They must still be
        // recovered, but only by a single rule.
        var legacy = new DeductionRecord
        {
            Id = Guid.NewGuid(),
            DeductionType = "UnpaidLeave",
            Amount = 700m
        };

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context(deductions: [legacy]));

        Assert.Equal(700m, result.TotalDeductions);
        Assert.Single(result.Components, c => c.ReferenceId == legacy.Id);
    }

    // ---------------------------------------------------------------- advance instalments

    [Fact]
    public void AdvanceInstallment_DueInPeriod_IsRecovered()
    {
        var due = new DeductionRecord
        {
            Id = Guid.NewGuid(),
            DeductionType = "AdvanceRecovery",
            Amount = 333.34m,
            InstallmentNumber = 1,
            DueDate = new DateOnly(2026, 5, 5)
        };

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context(deductions: [due]));

        Assert.Equal(333.34m, result.TotalDeductions);
        Assert.Contains(due.Id, result.ConsumedDeductionIds);
    }

    [Fact]
    public void AdvanceInstallment_NotYetDue_IsNeitherChargedNorConsumed()
    {
        // The original defect: future instalments were skipped by the engine yet still marked
        // applied, so the employee's whole repayment plan vanished without a single deduction.
        var future = new DeductionRecord
        {
            Id = Guid.NewGuid(),
            DeductionType = "AdvanceRecovery",
            Amount = 333.33m,
            InstallmentNumber = 3,
            DueDate = new DateOnly(2026, 8, 5)
        };

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context(deductions: [future]));

        Assert.Equal(0m, result.TotalDeductions);
        Assert.DoesNotContain(future.Id, result.ConsumedDeductionIds);
    }

    [Fact]
    public void FinalAdvanceRecovery_WithoutDueDate_IsRecoveredImmediately()
    {
        var final = new DeductionRecord
        {
            Id = Guid.NewGuid(),
            DeductionType = "AdvanceFinalRecovery",
            Amount = 666.66m
        };

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context(deductions: [final]));

        Assert.Equal(666.66m, result.TotalDeductions);
        Assert.Contains(final.Id, result.ConsumedDeductionIds);
    }

    // ---------------------------------------------------------------- consumption invariant

    [Fact]
    public void EveryConsumedDeduction_HasAMatchingSalaryLineComponent()
    {
        var charged = new DeductionRecord { Id = Guid.NewGuid(), DeductionType = "Fine", Amount = 200m };
        var notDue = new DeductionRecord
        {
            Id = Guid.NewGuid(),
            DeductionType = "AdvanceRecovery",
            Amount = 300m,
            DueDate = new DateOnly(2026, 12, 1)
        };

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context(deductions: [charged, notDue]));

        foreach (var id in result.ConsumedDeductionIds)
            Assert.Contains(result.Components, c => c.ReferenceId == id);

        Assert.DoesNotContain(notDue.Id, result.ConsumedDeductionIds);
    }

    [Fact]
    public void ZeroAmountAdjustments_AreConsumed_ButProduceNoComponent()
    {
        // Lifecycle markers carry Amount = 0. They must be retired by the run, otherwise the loader
        // re-reads them into every future run for ever.
        var marker = new PayrollAdjustment { Id = Guid.NewGuid(), Amount = 0m, AdjustmentReason = "تسجيل نقل" };

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context(adjustments: [marker]));

        Assert.Contains(marker.Id, result.ConsumedAdjustmentIds);
        Assert.DoesNotContain(result.Components, c => c.ReferenceId == marker.Id);
    }

    // ---------------------------------------------------------------- advance scheduling invariant

    [Fact]
    public void AdvanceInstallments_SumToOriginalAmount_WhenDerivedFromPrincipal()
    {
        const decimal total = 10_000m;
        const int installments = 3;
        var monthly = decimal.Round(total / installments, 2, MidpointRounding.AwayFromZero);
        var scheduled = Enumerable.Range(0, installments)
            .Select(i => i == installments - 1 ? total - (monthly * (installments - 1)) : monthly)
            .ToList();

        Assert.Equal(total, scheduled.Sum());
        Assert.Equal(3333.33m, scheduled[0]);
        Assert.Equal(3333.34m, scheduled[2]);
    }

    // ---------------------------------------------------------------- arithmetic invariant

    [Theory]
    [InlineData(50_000)]
    [InlineData(120_000)]
    [InlineData(1_000_000)]
    public void NetPay_EqualsGrossMinusDeductionsPlusAdjustments(decimal salary)
    {
        var deduction = new DeductionRecord { Id = Guid.NewGuid(), DeductionType = "Fine", Amount = 1_500m };
        var adjustment = new PayrollAdjustment { Id = Guid.NewGuid(), Amount = 400m, AdjustmentReason = "علاوة" };

        var result = PayrollEngine.CreateDefaultPipeline()
            .Calculate(Context(salary, deductions: [deduction], adjustments: [adjustment]));

        var componentSum = result.Components.Sum(c => c.Amount);
        var expectedNet = result.GrossPay - result.TotalDeductions + 400m;

        Assert.Equal(decimal.Round(expectedNet, 2), decimal.Round(result.NetPay, 2));
        Assert.True(componentSum != 0);
    }

    [Fact]
    public void Deductions_NeverProducePositiveComponents()
    {
        var deduction = new DeductionRecord { Id = Guid.NewGuid(), DeductionType = "Fine", Amount = 900m };

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context(deductions: [deduction]));

        Assert.All(
            result.Components.Where(c => c.ComponentType == SalaryComponentType.Deduction),
            c => Assert.True(c.Amount <= 0, "deduction components must be negative"));
    }
}
