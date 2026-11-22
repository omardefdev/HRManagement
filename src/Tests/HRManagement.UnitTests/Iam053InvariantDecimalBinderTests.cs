using System.Globalization;
using HRManagement.ServiceDefaults.Localization;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-53: an amount posted by a form is read as a numeric input sends it (a dot for the fraction), whatever the request's culture.
/// Under the service's Arabic culture the framework's binder refused «8250.75»; and a comma is never taken for a decimal point.
/// </summary>
public sealed class Iam053InvariantDecimalBinderTests
{
    private static decimal? Parse(string raw, string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return HrInvariantDecimalModelBinder.TryParse(raw, out var value) ? value : null;
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("ar-SY")]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void A_dot_fraction_is_read_the_same_under_every_culture(string culture)
    {
        Assert.Equal(8250.75m, Parse("8250.75", culture));
        Assert.Equal(4500m, Parse("4500", culture));
        Assert.Equal(0.01m, Parse(" 0.01 ", culture));
    }

    [Theory]
    [InlineData("ar-SY")]
    [InlineData("en-US")]
    public void What_is_not_a_number_is_refused(string culture)
    {
        Assert.Null(Parse("abc", culture));
        Assert.Null(Parse("12.5.1", culture));
        Assert.Null(Parse("", culture));
    }

    [Fact]
    public void A_comma_is_never_read_as_a_decimal_point_of_the_invariant_form()
    {
        // Under an Arabic culture «7500,50» is not 750050 and not 7500.50: it is refused (its separators are the Arabic ones).
        Assert.NotEqual(750050m, Parse("7500,50", "ar-SY"));
        Assert.NotEqual(7500.50m, Parse("7500,50", "ar-SY"));
        // Under a culture whose decimal separator is the comma, the culture's own form is accepted after the invariant one fails.
        Assert.Equal(7500.50m, Parse("7500,50", "fr-FR"));
    }
}
