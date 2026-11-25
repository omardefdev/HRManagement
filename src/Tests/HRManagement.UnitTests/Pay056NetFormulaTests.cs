using HRManagement.Documents.QuestPdf;
using HRManagement.Integration.Abstractions.Payroll;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-56 (G-PAY-03): one definition of the net. The engine, the validation, the legacy builder and every reader's shape give the
/// same net for the same line: gross − deductions + adjustments, floored at zero, rounded by the framework. A line with no adjustment
/// gives the number it always gave.
/// </summary>
public class Pay056NetFormulaTests
{
    private static PayrollConfiguration Framework(int decimalPlaces = 2) => new()
    {
        Code = "PAY056",
        ProrationDaysBasis = ProrationDaysBasis.WorkingDays,
        DecimalPlaces = decimalPlaces,
        // A version of the current fingerprint with no bracket is taxed by none (the built-in brackets are the legacy path's), so a
        // test's deductions are exactly the ones it plants.
        HashSchemaVersion = PayrollConfiguration.CurrentHashSchemaVersion,
        TaxBrackets = [],
    };

    private static PayrollCalculationContext Context(
        decimal baseSalary,
        IEnumerable<PayrollAdjustment>? adjustments = null,
        IEnumerable<DeductionRecord>? deductions = null,
        PayrollConfiguration? framework = null) => new()
        {
            PayrollRunId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PeriodStart = new DateOnly(2028, 1, 1),
            PeriodEnd = new DateOnly(2028, 1, 31),
            WorkingDaysInPeriod = 22,
            EmployeeWorkingDays = 22,
            MonthlyBaseSalary = baseSalary,
            Allowances = [],
            Deductions = deductions?.ToList() ?? [],
            StatutoryDeductionTypes = [],
            Adjustments = adjustments?.ToList() ?? [],
            AbsenceDeductions = [],
            Configuration = framework ?? Framework(),
            ExtraHolidays = new HashSet<DateOnly>(),
        };

    private static PayrollAdjustment Adjustment(decimal amount, string reason = "تسوية") =>
        new() { Id = Guid.NewGuid(), Amount = amount, AdjustmentReason = reason };

    private static DeductionRecord Deduction(decimal amount) =>
        new() { Id = Guid.NewGuid(), Amount = amount, DeductionType = "Manual" };

    private static Dictionary<Guid, List<SalaryLineComponent>> ByLine(PayrollCalculationResult result)
    {
        result.Line.Id = result.Line.Id == Guid.Empty ? Guid.NewGuid() : result.Line.Id;
        return new Dictionary<Guid, List<SalaryLineComponent>> { [result.Line.Id] = result.Components };
    }

    // --- the definition ---

    [Theory]
    [InlineData(1000, 250, 0, 750)]            // no adjustment: the number it always was
    [InlineData(1000, 250, 50, 800)]           // a positive adjustment
    [InlineData(1000, 250, -100, 650)]         // a negative adjustment
    [InlineData(1000, 250, -900, 0)]           // an adjustment that takes more than the line has: floored at zero
    [InlineData(1000, 1500, 0, 0)]             // deductions above the gross: floored at zero, as before
    [InlineData(1000, -200, 0, 1200)]          // negative deductions (an attendance recovery, PLAN-57): money back
    [InlineData(0, 0, 500, 500)]               // adjustments alone
    public void The_net_is_gross_minus_deductions_plus_adjustments_floored_at_zero(
        decimal gross, decimal deductions, decimal adjustments, decimal expected)
    {
        Assert.Equal(expected, PayrollNet.Net(gross, deductions, adjustments, PayrollNet.LegacyRound));
        Assert.Equal(expected, PayrollNet.Net(gross, deductions, adjustments, Framework()));
        Assert.Equal(expected, PayrollNet.Net(new SalaryLine { GrossPay = gross, TotalDeductions = deductions, TotalAdjustments = adjustments }));
    }

    [Fact]
    public void The_unclipped_sum_keeps_what_the_floor_hides_and_the_shortfall_names_it()
    {
        var line = new SalaryLine { GrossPay = 0m, TotalDeductions = 0m, TotalAdjustments = -30_000m };
        Assert.Equal(-30_000m, PayrollNet.Unclipped(line));
        Assert.Equal(0m, PayrollNet.Net(line));
        Assert.Equal(30_000m, PayrollNet.Shortfall(line));
        Assert.Equal(0m, PayrollNet.Shortfall(new SalaryLine { GrossPay = 10m, TotalAdjustments = -10m }));
        Assert.Equal(0m, PayrollNet.Shortfall(new SalaryLine { GrossPay = 10m }));
    }

    [Fact]
    public void The_net_is_rounded_by_the_framework()
    {
        // A framework that pays whole units rounds the net of an adjustment with decimals; the parts keep their own values.
        Assert.Equal(1_051m, PayrollNet.Net(1_000m, 0m, 50.50m, Framework(decimalPlaces: 0)));
        Assert.Equal(1_050.5m, PayrollNet.Net(1_000m, 0m, 50.50m, Framework(decimalPlaces: 2)));
    }

    // --- the engine ---

    [Fact]
    public void The_engine_writes_the_adjustments_on_the_line_and_its_net_is_the_one_net()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var result = engine.Calculate(Context(900_000m, [Adjustment(50_000m, "فرق نقل"), Adjustment(-20_000m, "تسوية يدوية")], [Deduction(100_000m)]));

        Assert.Equal(900_000m, result.Line.GrossPay);
        Assert.Equal(100_000m, result.Line.TotalDeductions);
        Assert.Equal(30_000m, result.Line.TotalAdjustments);
        Assert.Equal(830_000m, result.Line.NetPay);
        Assert.Equal(PayrollNet.Net(result.Line), result.Line.NetPay);
        // The components add up to the same parts.
        Assert.Equal(PayrollNet.Unclipped(result.Line), result.Components.Sum(c => c.Amount));
    }

    [Fact]
    public void An_adjustment_is_outside_the_gross_and_the_deductions()
    {
        // D-PAY-17: the tax and insurance base is the gross; an adjustment changes neither it nor the deductions.
        var engine = PayrollEngine.CreateDefaultPipeline();
        var without = engine.Calculate(Context(900_000m));
        var with = engine.Calculate(Context(900_000m, [Adjustment(50_000m)]));

        Assert.Equal(without.Line.GrossPay, with.Line.GrossPay);
        Assert.Equal(without.Line.TotalDeductions, with.Line.TotalDeductions);
        Assert.Equal(without.Line.NetPay + 50_000m, with.Line.NetPay);
    }

    [Fact]
    public void A_line_with_no_adjustment_gives_the_number_it_always_gave()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var result = engine.Calculate(Context(10_000m, deductions: [Deduction(1_250m)]));

        Assert.Equal(0m, result.Line.TotalAdjustments);
        // The formula of every release before this plan, written out: max(0, gross − deductions), two places, away from zero.
        var before = decimal.Round(Math.Max(0m, result.Line.GrossPay + (-result.Line.TotalDeductions)), 2, MidpointRounding.AwayFromZero);
        Assert.Equal(before, result.Line.NetPay);
        Assert.Equal(8_750m, result.Line.NetPay);
    }

    [Fact]
    public void A_zero_amount_adjustment_is_consumed_and_adds_nothing()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var marker = Adjustment(0m, "تسجيل نقل");
        var result = engine.Calculate(Context(10_000m, [marker]));

        Assert.Contains(marker.Id, result.ConsumedAdjustmentIds);
        Assert.Equal(0m, result.Line.TotalAdjustments);
        Assert.DoesNotContain(result.Components, c => c.ComponentType == SalaryComponentType.Adjustment);
    }

    [Fact]
    public void A_clipped_line_keeps_its_parts_and_pays_nothing()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var result = engine.Calculate(Context(10_000m, [Adjustment(-25_000m)]));

        Assert.Equal(-25_000m, result.Line.TotalAdjustments);
        Assert.Equal(0m, result.Line.NetPay);
        Assert.Equal(-15_000m, PayrollNet.Unclipped(result.Line));
        Assert.Equal(PayrollNet.Unclipped(result.Line), result.Components.Sum(c => c.Amount));
    }

    // --- the validation ---

    [Theory]
    [InlineData(50_000)]
    [InlineData(-20_000)]
    [InlineData(0)]
    [InlineData(-2_000_000)] // clipped
    public void The_validation_accepts_what_the_engine_produced_with_any_adjustment(decimal adjustment)
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var framework = Framework();
        var adjustments = adjustment == 0 ? Array.Empty<PayrollAdjustment>() : [Adjustment(adjustment)];
        var result = engine.Calculate(Context(900_000m, adjustments, [Deduction(100_000m)], framework));

        var (ok, error) = PayrollRunReconciliation.ValidateLineIntegrity([result.Line], ByLine(result), round: framework.RoundMoney);
        Assert.True(ok, error);
    }

    [Fact]
    public void The_validation_accepts_a_whole_unit_framework_with_an_adjustment_that_has_decimals()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var framework = Framework(decimalPlaces: 0);
        var result = engine.Calculate(Context(900_000m, [Adjustment(1_000.50m)], framework: framework));

        Assert.Equal(901_001m, result.Line.NetPay);
        var (ok, error) = PayrollRunReconciliation.ValidateLineIntegrity([result.Line], ByLine(result), round: framework.RoundMoney);
        Assert.True(ok, error);
    }

    [Fact]
    public void The_validation_accepts_negative_deductions()
    {
        // PLAN-57 §4 rule 5: an attendance recovery is a deduction component with a positive amount, which makes TotalDeductions negative.
        var line = new SalaryLine { Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), GrossPay = 1_000m, TotalDeductions = -200m, NetPay = 1_200m };
        var components = new Dictionary<Guid, List<SalaryLineComponent>>
        {
            [line.Id] =
            [
                new SalaryLineComponent { ComponentType = SalaryComponentType.BaseSalary, ComponentName = "الراتب الأساسي", Amount = 1_000m },
                new SalaryLineComponent { ComponentType = SalaryComponentType.Deduction, ComponentName = "استرداد خصم غياب", Amount = 200m },
            ],
        };

        var (ok, error) = PayrollRunReconciliation.ValidateLineIntegrity([line], components);
        Assert.True(ok, error);
    }

    [Fact]
    public void The_validation_refuses_a_net_that_is_not_the_one_net()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var result = engine.Calculate(Context(900_000m, [Adjustment(50_000m)]));
        var components = ByLine(result);

        // The net an older posting wrote: gross − deductions, the adjustment dropped.
        result.Line.NetPay = result.Line.GrossPay + (-result.Line.TotalDeductions);
        var (ok, error) = PayrollRunReconciliation.ValidateLineIntegrity([result.Line], components);
        Assert.False(ok);
        Assert.Contains("صافي السطر", error);
    }

    [Fact]
    public void The_validation_refuses_a_negative_net()
    {
        var line = new SalaryLine { Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), GrossPay = 0m, TotalDeductions = 0m, TotalAdjustments = -500m, NetPay = -500m };
        var components = new Dictionary<Guid, List<SalaryLineComponent>>
        {
            [line.Id] = [new SalaryLineComponent { ComponentType = SalaryComponentType.Adjustment, ComponentName = "تسوية", Amount = -500m, ReferenceId = Guid.NewGuid() }],
        };

        var (ok, _) = PayrollRunReconciliation.ValidateLineIntegrity([line], components);
        Assert.False(ok);
    }

    [Fact]
    public void The_validation_refuses_components_that_do_not_add_up_to_the_parts()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var result = engine.Calculate(Context(900_000m, [Adjustment(50_000m)]));
        var components = ByLine(result);
        components[result.Line.Id].RemoveAll(c => c.ComponentType == SalaryComponentType.Adjustment);

        var (ok, error) = PayrollRunReconciliation.ValidateLineIntegrity([result.Line], components);
        Assert.False(ok);
        Assert.Contains("مكوّنات", error);
    }

    [Fact]
    public void The_run_total_is_the_sum_of_the_one_net()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var first = engine.Calculate(Context(900_000m, [Adjustment(50_000m)]));
        var second = engine.Calculate(Context(700_000m, [Adjustment(-20_000m)]));
        first.Line.Id = Guid.NewGuid();
        second.Line.Id = Guid.NewGuid();
        var components = new Dictionary<Guid, List<SalaryLineComponent>> { [first.Line.Id] = first.Components, [second.Line.Id] = second.Components };
        var run = new PayrollRun { TotalNetPay = first.Line.NetPay + second.Line.NetPay };

        var (ok, error) = PayrollRunReconciliation.ValidateTotals(run, [first.Line, second.Line], components);
        Assert.True(ok, error);
        Assert.Equal(950_000m + 680_000m, run.TotalNetPay);

        run.TotalNetPay -= 30_000m; // the total a posting without adjustments would have written
        Assert.False(PayrollRunReconciliation.ValidateTotals(run, [first.Line, second.Line], components).Ok);
    }

    // --- every other builder and reader ---

    [Fact]
    public void The_legacy_builder_gives_the_same_net_and_carries_the_adjustments()
    {
        var (line, components) = PayrollCalculation.GenerateSalaryLine(
            Guid.NewGuid(), Guid.NewGuid(), 10_000m, [], [Deduction(1_000m)], [], [Adjustment(500m), Adjustment(-200m)]);

        Assert.Equal(300m, line.TotalAdjustments);
        Assert.Equal(9_300m, line.NetPay);
        Assert.Equal(PayrollNet.Net(line), line.NetPay);
        Assert.Equal(PayrollNet.Unclipped(line), components.Sum(c => c.Amount));
    }

    [Fact]
    public void Two_runs_are_the_same_only_if_their_adjustments_are_too()
    {
        static PayrollDeterminismComparer.CanonicalLine Line(Guid employee, decimal adjustments) =>
            new(employee, 1_000m, 100m, 900m + adjustments, [], adjustments);

        var employee = Guid.NewGuid();
        Assert.True(PayrollDeterminismComparer.AreBitForBitEqual([Line(employee, 50m)], [Line(employee, 50m)]));
        Assert.False(PayrollDeterminismComparer.AreBitForBitEqual([Line(employee, 50m)], [Line(employee, 0m)]));
        Assert.NotNull(PayrollDeterminismComparer.BuildDiff([Line(employee, 50m)], [Line(employee, 0m)]));
    }

    [Fact]
    public void The_shapes_a_reader_gets_carry_the_adjustments_last()
    {
        // A client that reads a shape by position keeps every member it knew: the adjustments are the last member of each.
        Assert.Equal("TotalAdjustments", typeof(PayrollDisbursementLine).GetConstructors().Single().GetParameters().Last().Name);
        Assert.Equal("TotalAdjustments", typeof(PayslipPdfModel).GetConstructors().Single().GetParameters().Last().Name);
        Assert.Equal(
            "TotalAdjustments",
            typeof(Payroll.Application.Dtos.SalaryLineDetailsDto).GetConstructors().Single().GetParameters().Last().Name);
    }
}
