using System.Globalization;

namespace HRManagement.IntegrationTests;

/// <summary>QA-034 (AC-QA-34h): tests run under ar-SY with the Gregorian calendar on every machine.</summary>
public class TestCultureGuardTests
{
    [Fact]
    public void Test_thread_culture_is_ar_SY_gregorian()
    {
        Assert.Equal(TestCulture.Name, CultureInfo.CurrentCulture.Name);
        Assert.Equal(TestCulture.Name, CultureInfo.CurrentUICulture.Name);
        Assert.IsType<GregorianCalendar>(CultureInfo.CurrentCulture.Calendar);
        Assert.Equal("2026", new DateTime(2026, 10, 30).ToString("yyyy"));
    }

    [Fact]
    public async Task Thread_pool_threads_inherit_the_test_culture()
    {
        Assert.Equal(TestCulture.Name, CultureInfo.DefaultThreadCurrentCulture?.Name);
        var name = await Task.Run(() => CultureInfo.CurrentCulture.Name);
        Assert.Equal(TestCulture.Name, name);
    }
}
