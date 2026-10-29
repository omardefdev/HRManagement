using HR.Core.Domain.Services.RecruitmentKpi;

namespace HRManagement.UnitTests;

/// <summary>HCM-032: median, P90 nearest-rank, rates and day counting of the recruitment KPIs.</summary>
public class Hcm032KpiStatisticsTests
{
    [Fact]
    public void Median_of_odd_count_is_the_middle_value() =>
        Assert.Equal(7m, KpiStatistics.Median([9, 3, 7, 1, 12]));

    [Fact]
    public void Median_of_even_count_is_the_mean_of_the_two_middle_values_with_one_decimal()
    {
        Assert.Equal(5.5m, KpiStatistics.Median([1, 5, 6, 30]));
        // (2 + 3) / 2 = 2.5; (10 + 11) / 2 = 10.5
        Assert.Equal(2.5m, KpiStatistics.Median([3, 2]));
        Assert.Equal(10.5m, KpiStatistics.Median([10, 11, 1, 40]));
    }

    [Fact]
    public void Median_rounds_to_one_decimal_away_from_zero() =>
        // (1.25 + 1.3) / 2 = 1.275 → 1.3
        Assert.Equal(1.3m, KpiStatistics.Median([1.25m, 1.3m]));

    [Fact]
    public void Empty_input_has_no_value()
    {
        Assert.Null(KpiStatistics.Median(Array.Empty<int>()));
        Assert.Null(KpiStatistics.P90(Array.Empty<int>()));
        Assert.Equal("—", KpiStatistics.Format(null));
    }

    [Fact]
    public void P90_of_one_value_is_that_value() => Assert.Equal(4m, KpiStatistics.P90([4]));

    [Fact]
    public void P90_of_nine_values_is_the_ninth() =>
        // ceil(0.9 × 9) = ceil(8.1) = 9 → the largest
        Assert.Equal(90m, KpiStatistics.P90([10, 20, 30, 40, 50, 60, 70, 80, 90]));

    [Fact]
    public void P90_of_ten_values_is_the_ninth() =>
        // ceil(0.9 × 10) = 9
        Assert.Equal(9m, KpiStatistics.P90([10, 9, 8, 7, 6, 5, 4, 3, 2, 1]));

    [Fact]
    public void P90_of_eleven_values_is_the_tenth() =>
        // ceil(0.9 × 11) = ceil(9.9) = 10
        Assert.Equal(10m, KpiStatistics.P90([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]));

    [Fact]
    public void Rate_is_a_percentage_with_one_decimal_and_none_for_zero_denominator()
    {
        Assert.Equal(66.7m, KpiStatistics.Rate(2, 3));
        Assert.Equal(100m, KpiStatistics.Rate(4, 4));
        Assert.Equal(0m, KpiStatistics.Rate(0, 7));
        Assert.Null(KpiStatistics.Rate(3, 0));
        Assert.Equal("—", KpiStatistics.FormatPercent(KpiStatistics.Rate(3, 0)));
        Assert.Equal("66.7%", KpiStatistics.FormatPercent(KpiStatistics.Rate(2, 3)));
    }

    [Fact]
    public void Calendar_days_count_utc_dates_not_hours()
    {
        var late = new DateTime(2026, 10, 1, 23, 50, 0, DateTimeKind.Utc);
        var early = new DateTime(2026, 10, 2, 0, 10, 0, DateTimeKind.Utc);
        Assert.Equal(1, KpiStatistics.CalendarDays(late, early));
        Assert.Equal(0, KpiStatistics.CalendarDays(early, early.AddHours(20)));
        Assert.Equal(31, KpiStatistics.CalendarDays(new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)));
    }

    [Fact]
    public void Formatting_is_invariant_under_an_arabic_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SY");
            Assert.Equal("12.5", KpiStatistics.Format(12.5m));
            Assert.Equal("33.3%", KpiStatistics.FormatPercent(KpiStatistics.Rate(1, 3)));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "<5")]
    [InlineData(4, "<5")]
    [InlineData(5, "5")]
    [InlineData(37, "37")]
    public void Small_cells_from_one_to_four_are_suppressed(int count, string shown) =>
        Assert.Equal(shown, SmallCellSuppression.Display(count, 5));

    [Fact]
    public void A_rate_over_a_suppressed_cell_is_suppressed()
    {
        Assert.Equal("<5", SmallCellSuppression.DisplayRate(3, 20, 5));
        Assert.Equal("<5", SmallCellSuppression.DisplayRate(0, 4, 5));
        Assert.Equal("25%", SmallCellSuppression.DisplayRate(5, 20, 5));
        Assert.Null(SmallCellSuppression.Value(2, 5));
        Assert.Equal(0, SmallCellSuppression.Value(0, 5));
        Assert.Equal("<5", SmallCellSuppression.Display(3, 0));
    }
}
