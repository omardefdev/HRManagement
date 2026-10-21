using HRManagement.ServiceDefaults.Integration;

namespace HRManagement.UnitTests;

public class HrLookupDisplayHelperTests
{
    [Theory]
    [InlineData("أحمد محمد", true)]
    [InlineData("غير متاح", false)]
    [InlineData("جاري التحميل...", false)]
    [InlineData("a0000001-0000-4000-8000-000000000201", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsResolvedEmployeeName_distinguishes_real_names_from_placeholders(string? value, bool expected) =>
        Assert.Equal(expected, HrLookupDisplayHelper.IsResolvedEmployeeName(value));
}
