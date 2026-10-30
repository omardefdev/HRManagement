using System.Globalization;

namespace HRManagement.UnitTests;

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

public class TestCultureInitializerPresenceTests
{
    [Theory]
    [InlineData("HRManagement.UnitTests")]
    [InlineData("HRManagement.IntegrationTests")]
    [InlineData("HRManagement.AcceptanceTests")]
    public void Each_test_project_sets_the_culture_in_a_module_initializer(string project)
    {
        var file = Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "src", "Tests", project, "TestCulture.cs");
        Assert.True(System.IO.File.Exists(file), $"{project} lacks TestCulture.cs");
        var text = System.IO.File.ReadAllText(file);
        Assert.Contains("[ModuleInitializer]", text, StringComparison.Ordinal);
        Assert.Contains("\"ar-SY\"", text, StringComparison.Ordinal);
        Assert.Contains("DefaultThreadCurrentCulture", text, StringComparison.Ordinal);
    }
}
