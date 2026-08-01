using Payroll.Domain.Entities;
using Payroll.Domain.Services;

namespace HRManagement.UnitTests;

public class PayrollPeriodRulesTests
{
    [Fact]
    public void CountWorkingDays_Excludes_FriSat()
    {
        var start = new DateOnly(2026, 5, 25);
        var end = new DateOnly(2026, 5, 31);
        Assert.Equal(5, PayrollPeriodRules.CountWorkingDays(start, end));
    }

    [Fact]
    public void IsOfficialHoliday_Uses_ExtraHolidaySet()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 1, 1), new(2026, 5, 1) };
        Assert.True(PayrollPeriodRules.IsOfficialHoliday(new DateOnly(2026, 1, 1), holidays));
        Assert.True(PayrollPeriodRules.IsOfficialHoliday(new DateOnly(2026, 5, 1), holidays));
        Assert.False(PayrollPeriodRules.IsOfficialHoliday(new DateOnly(2026, 3, 15), holidays));
    }

    [Fact]
    public void ParseNonWorkingDays_Reads_ConfigCsv()
    {
        var days = PayrollPeriodRules.ParseNonWorkingDays("Friday,Saturday");
        Assert.Contains(DayOfWeek.Friday, days);
        Assert.Contains(DayOfWeek.Saturday, days);
    }

    [Fact]
    public void ProrateMonthlyAmount_Returns_Expected()
    {
        var amount = PayrollPeriodRules.ProrateMonthlyAmount(30000m, 22, 3);
        Assert.Equal(4090.91m, amount);
    }

    [Fact]
    public void ValidatePayrollPeriodCoverage_Rejects_AllHolidayWeek()
    {
        var err = PayrollPeriodRules.ValidatePayrollPeriodCoverage(
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 1));
        Assert.NotNull(err);
    }
}
