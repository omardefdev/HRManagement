using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>SYR-014: tests for Syrian holiday calendar policy structure.</summary>
public class Syr014CalendarTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr014-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Policy_uses_asia_damascus_timezone()
    {
        var path = RepoPaths.Resolve("docs", "syr", "holiday-calendar-policy.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("SYR-014", root.GetProperty("card").GetString());
        Assert.Equal("Asia/Damascus", root.GetProperty("timezone").GetString());
    }

    [Fact]
    public void Policy_references_decree_188_2025()
    {
        var path = RepoPaths.Resolve("docs", "syr", "holiday-calendar-policy.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("SYR-DEC-188-2025", doc.RootElement.GetProperty("legalSourceId").GetString());
    }

    [Fact]
    public void Policy_forbids_hardcoded_holiday_list()
    {
        var path = RepoPaths.Resolve("docs", "syr", "holiday-calendar-policy.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("forbidHardcodedHolidayList").GetBoolean());
    }

    [Fact]
    public void Policy_defines_fixed_and_variable_holidays()
    {
        var path = RepoPaths.Resolve("docs", "syr", "holiday-calendar-policy.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var fixedHolidays = doc.RootElement.GetProperty("fixedHolidays").EnumerateArray().ToList();
        Assert.True(fixedHolidays.Count >= 5);

        var variable = doc.RootElement.GetProperty("variableHolidays");
        Assert.True(variable.GetProperty("retroactiveApplicationAllowed").GetBoolean());
        Assert.True(variable.GetProperty("examples").EnumerateArray().Count() >= 2);
    }

    [Fact]
    public void Continuous_operations_forbid_24x7_closure()
    {
        var path = RepoPaths.Resolve("docs", "syr", "holiday-calendar-policy.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var ops = doc.RootElement.GetProperty("continuousOperations");

        Assert.True(ops.GetProperty("forbidFullClosureFor24x7").GetBoolean());
        Assert.True(ops.GetProperty("shiftCrossesMidnightSupported").GetBoolean());
    }

    [Fact]
    public void Payroll_recalculation_required_before_approval()
    {
        var path = RepoPaths.Resolve("docs", "syr", "holiday-calendar-policy.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var impact = doc.RootElement.GetProperty("payrollImpact");

        Assert.True(impact.GetProperty("recalculateBeforePayrollApproval").GetBoolean());
        Assert.True(impact.GetProperty("retroactiveHolidayTriggersRecalc").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_014_CALENDAR.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "holiday-calendar-policy.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr014-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
