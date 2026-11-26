using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-57 (§5): the absence rule with attendance differences. A negative difference (an absence corrected to presence) is a refund:
/// a deduction component with a positive amount that lowers the total deductions, even below zero; the one net (<see cref="PayrollNet"/>)
/// and the validation (<see cref="PayrollRunReconciliation"/>) hold. A difference carried into a later period's run names its original
/// period. Every row the rule reads is consumed. A row of before this plan gives the number and the name it always gave.
/// </summary>
public class Lva057AbsenceRuleRefundTests
{
    private static readonly DateOnly RunStart = new(2028, 1, 1);
    private static readonly DateOnly RunEnd = new(2028, 1, 31);
    private static readonly DateOnly December = new(2027, 12, 1);
    private static readonly DateOnly DecemberEnd = new(2027, 12, 31);

    // 880,000 over 22 working days: one day is 40,000.
    private const decimal BaseSalary = 880_000m;
    private const decimal Day = 40_000m;

    private static PayrollConfiguration Framework() => new()
    {
        Code = "LVA057",
        ProrationDaysBasis = ProrationDaysBasis.WorkingDays,
        DecimalPlaces = 2,
        // A version of the current fingerprint with no bracket is taxed by none, so a test's deductions are exactly the ones it plants.
        HashSchemaVersion = PayrollConfiguration.CurrentHashSchemaVersion,
        TaxBrackets = [],
    };

    private static PayrollCalculationContext Context(IEnumerable<AbsenceDeduction> absences, IEnumerable<DeductionRecord>? deductions = null) => new()
    {
        PayrollRunId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        PeriodStart = RunStart,
        PeriodEnd = RunEnd,
        WorkingDaysInPeriod = 22,
        EmployeeWorkingDays = 22,
        MonthlyBaseSalary = BaseSalary,
        Allowances = [],
        Deductions = deductions?.ToList() ?? [],
        StatutoryDeductionTypes = [],
        Adjustments = [],
        AbsenceDeductions = absences.ToList(),
        Configuration = Framework(),
        ExtraHolidays = new HashSet<DateOnly>(),
    };

    /// <summary>A difference row as payroll's consumer writes it for an attendance summary version.</summary>
    private static AbsenceDeduction Difference(int days, DateOnly periodStart, DateOnly periodEnd, int version = 2, bool carried = false) => new()
    {
        Id = Guid.NewGuid(),
        PeriodStart = periodStart,
        PeriodEnd = periodEnd,
        OriginalPeriodStart = periodStart,
        OriginalPeriodEnd = periodEnd,
        AbsentDays = days,
        Amount = 0,
        Reason = version == 1 ? $"خصم غياب — فترة الحضور {periodStart:yyyy-MM}" : $"فرق غياب بتصحيح الحضور — فترة {periodStart:yyyy-MM}",
        SourceReference = AbsenceDeduction.AttendanceReference(Guid.NewGuid(), version),
        SourceVersion = version,
        CarryForward = carried
    };

    private static (bool Ok, string? Error) Validate(PayrollCalculationResult result)
    {
        result.Line.Id = result.Line.Id == Guid.Empty ? Guid.NewGuid() : result.Line.Id;
        return PayrollRunReconciliation.ValidateLineIntegrity(
            [result.Line], new Dictionary<Guid, List<SalaryLineComponent>> { [result.Line.Id] = result.Components }, round: Framework().RoundMoney);
    }

    [Fact]
    public void A_refund_with_no_other_deduction_makes_the_total_deductions_negative_and_the_net_and_the_validation_hold()
    {
        var refund = Difference(-1, December, DecemberEnd, carried: true);

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context([refund]));

        // The refund is a deduction component with a positive amount, named for the period the absence was deducted for.
        var component = Assert.Single(result.Components, c => c.ReferenceId == refund.Id);
        Assert.Equal(SalaryComponentType.Deduction, component.ComponentType);
        Assert.Equal(Day, component.Amount);
        Assert.Equal("استرداد خصم غياب — فترة 2027-12", component.ComponentName);
        // Money back: the total deductions fall below zero, and the net is the gross plus the refund.
        Assert.Equal(BaseSalary, result.Line.GrossPay);
        Assert.Equal(-Day, result.Line.TotalDeductions);
        Assert.Equal(BaseSalary + Day, result.Line.NetPay);
        Assert.Equal(PayrollNet.Net(result.Line), result.Line.NetPay);
        Assert.Equal(PayrollNet.Unclipped(result.Line), result.Components.Sum(c => c.Amount));
        var (ok, error) = Validate(result);
        Assert.True(ok, error);
        // The refund is consumed: it is given once.
        Assert.Contains(refund.Id, result.ConsumedAbsenceIds);
    }

    [Fact]
    public void A_close_of_two_days_and_a_correction_of_minus_one_in_the_same_run_deduct_one_day_net()
    {
        var close = Difference(2, RunStart, RunEnd, version: 1);
        var correction = Difference(-1, RunStart, RunEnd, version: 2);

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context([close, correction]));

        Assert.Equal(-2 * Day, result.Components.Single(c => c.ReferenceId == close.Id).Amount);
        Assert.Equal(Day, result.Components.Single(c => c.ReferenceId == correction.Id).Amount);
        Assert.Equal(Day, result.Line.TotalDeductions);
        Assert.Equal(BaseSalary - Day, result.Line.NetPay);
        Assert.Equal(new[] { close.Id, correction.Id }.Order(), result.ConsumedAbsenceIds.Order());
        var (ok, error) = Validate(result);
        Assert.True(ok, error);
        // A row of the run's own period keeps its reason as the item's name.
        Assert.Equal("خصم غياب — فترة الحضور 2028-01", result.Components.Single(c => c.ReferenceId == close.Id).ComponentName);
        Assert.Equal("استرداد خصم غياب — فترة 2028-01", result.Components.Single(c => c.ReferenceId == correction.Id).ComponentName);
    }

    [Fact]
    public void A_difference_carried_into_a_later_periods_run_names_its_original_period_and_is_paid_at_this_runs_wage()
    {
        var carried = Difference(1, December, DecemberEnd, carried: true);

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context([carried]));

        var component = Assert.Single(result.Components, c => c.ReferenceId == carried.Id);
        Assert.Equal("فرق غياب عن فترة 2027-12", component.ComponentName);
        // D-LVA-14: the day is valued by the run that applies it.
        Assert.Equal(-Day, component.Amount);
        Assert.Equal(Day, result.Line.TotalDeductions);
        Assert.Contains(carried.Id, result.ConsumedAbsenceIds);
        var (ok, error) = Validate(result);
        Assert.True(ok, error);
    }

    [Fact]
    public void Every_row_the_rule_reads_is_consumed_whatever_it_amounts_to()
    {
        var zero = new AbsenceDeduction { Id = Guid.NewGuid(), PeriodStart = RunStart, PeriodEnd = RunEnd, AbsentDays = 0, Amount = 0, Reason = "بلا أثر" };
        var deduction = Difference(1, RunStart, RunEnd, version: 1);
        var refund = Difference(-1, December, DecemberEnd, carried: true);

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context([zero, deduction, refund]));

        // Before PLAN-57 a row with no positive amount was skipped without being consumed, and stayed free for ever.
        Assert.Equal(new[] { zero.Id, deduction.Id, refund.Id }.Order(), result.ConsumedAbsenceIds.Order());
        Assert.DoesNotContain(result.Components, c => c.ReferenceId == zero.Id);
        // One day deducted and one refunded: nothing in total.
        Assert.Equal(0m, result.Line.TotalDeductions);
        Assert.Equal(BaseSalary, result.Line.NetPay);
    }

    [Fact]
    public void A_row_of_before_this_plan_gives_the_amount_and_the_name_it_always_gave()
    {
        // The old close: no original period, a zero amount, days, and the technical reason as the name.
        var oldClose = new AbsenceDeduction
        {
            Id = Guid.NewGuid(),
            PeriodStart = RunStart,
            PeriodEnd = RunEnd,
            AbsentDays = 2,
            Amount = 0,
            Reason = "AttendancePeriodClosed:5f0c"
        };
        // Unpaid leave: a stored amount, charged as it is.
        var unpaidLeave = new AbsenceDeduction
        {
            Id = Guid.NewGuid(),
            PeriodStart = RunStart,
            PeriodEnd = RunEnd,
            AbsentDays = 3,
            Amount = 99_999.99m,
            Reason = "إجازة بلا أجر",
            SourceReference = "LeaveRequestId=7c1d"
        };
        var noReason = new AbsenceDeduction { Id = Guid.NewGuid(), PeriodStart = RunStart, PeriodEnd = RunEnd, AbsentDays = 1, Amount = 0 };

        var result = PayrollEngine.CreateDefaultPipeline().Calculate(Context([oldClose, unpaidLeave, noReason]));

        var first = result.Components.Single(c => c.ReferenceId == oldClose.Id);
        Assert.Equal((-2 * Day, "AttendancePeriodClosed:5f0c"), (first.Amount, first.ComponentName));
        var second = result.Components.Single(c => c.ReferenceId == unpaidLeave.Id);
        Assert.Equal((-99_999.99m, "إجازة بلا أجر"), (second.Amount, second.ComponentName));
        Assert.Equal("خصم غياب", result.Components.Single(c => c.ReferenceId == noReason.Id).ComponentName);
        Assert.Equal(3 * Day + 99_999.99m, result.Line.TotalDeductions);
        Assert.Equal(3, result.ConsumedAbsenceIds.Count);
        var (ok, error) = Validate(result);
        Assert.True(ok, error);
    }

    [Fact]
    public void A_refund_beside_other_deductions_lowers_them_and_never_touches_the_gross()
    {
        var refund = Difference(-2, December, DecemberEnd, carried: true);
        var advance = new DeductionRecord { Id = Guid.NewGuid(), Amount = 100_000m, DeductionType = "Manual" };

        var without = PayrollEngine.CreateDefaultPipeline().Calculate(Context([], [advance]));
        var with = PayrollEngine.CreateDefaultPipeline().Calculate(Context([refund], [advance]));

        Assert.Equal(without.Line.GrossPay, with.Line.GrossPay);
        Assert.Equal(without.Line.TotalDeductions - 2 * Day, with.Line.TotalDeductions);
        Assert.Equal(without.Line.NetPay + 2 * Day, with.Line.NetPay);
        var (ok, error) = Validate(with);
        Assert.True(ok, error);
    }

    [Fact]
    public void The_key_of_an_attendance_difference_row_names_the_summary_and_the_version()
    {
        var summary = Guid.Parse("7a1f0c2e-5b3d-4e6f-8a90-1b2c3d4e5f60");

        Assert.Equal("ATT:7a1f0c2e-5b3d-4e6f-8a90-1b2c3d4e5f60:v3", AbsenceDeduction.AttendanceReference(summary, 3));
        Assert.True(Difference(1, RunStart, RunEnd).IsAttendanceDifference);
        Assert.False(new AbsenceDeduction { PeriodStart = RunStart, PeriodEnd = RunEnd }.IsAttendanceDifference);
    }
}
