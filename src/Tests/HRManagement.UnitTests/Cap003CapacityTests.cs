using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>CAP-003: tests for 3-year capacity forecast schema and constraints.</summary>
public class Cap003CapacityTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "cap003-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Forecast_covers_36_months()
    {
        var path = RepoPaths.Resolve("docs", "cap", "capacity-forecast.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("CAP-003", root.GetProperty("card").GetString());

        var horizons = root.GetProperty("forecastHorizons").EnumerateArray().ToList();
        Assert.True(horizons.Count >= 3);
        Assert.Equal(36, horizons.Last().GetProperty("months").GetInt32());
    }

    [Fact]
    public void Forecast_has_at_least_5_resource_types()
    {
        var path = RepoPaths.Resolve("docs", "cap", "capacity-forecast.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var resources = doc.RootElement.GetProperty("resources").EnumerateArray().ToList();
        Assert.True(resources.Count >= 5);

        var ids = resources.Select(r => r.GetProperty("id").GetString()).ToList();
        Assert.Contains("cpu", ids);
        Assert.Contains("memory", ids);
        Assert.Contains("database", ids);
        Assert.Contains("objectStorage", ids);
        Assert.Contains("network", ids);
    }

    [Fact]
    public void Every_resource_has_trigger_and_owner()
    {
        var path = RepoPaths.Resolve("docs", "cap", "capacity-forecast.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        foreach (var resource in doc.RootElement.GetProperty("resources").EnumerateArray())
        {
            var id = resource.GetProperty("id").GetString();
            Assert.True(resource.GetProperty("triggerThresholdPercent").GetInt32() >= 50,
                $"{id} trigger must be >= 50%");
            Assert.False(string.IsNullOrEmpty(resource.GetProperty("triggerOwner").GetString()),
                $"{id} must have a trigger owner");
            Assert.False(string.IsNullOrEmpty(resource.GetProperty("fundingLink").GetString()),
                $"{id} must link to FIN-001 funding");
            Assert.True(resource.GetProperty("procurementLeadWeeks").GetInt32() >= 1,
                $"{id} must define procurement lead time");
        }
    }

    [Fact]
    public void DR_capacity_is_defined()
    {
        var path = RepoPaths.Resolve("docs", "cap", "capacity-forecast.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var dr = doc.RootElement.GetProperty("drCapacity");
        Assert.Contains("100%", dr.GetProperty("requirement").GetString());
        Assert.False(string.IsNullOrEmpty(dr.GetProperty("rpoTarget").GetString()));
        Assert.False(string.IsNullOrEmpty(dr.GetProperty("rtoTarget").GetString()));
    }

    [Fact]
    public void Seasonal_peaks_are_defined()
    {
        var path = RepoPaths.Resolve("docs", "cap", "capacity-forecast.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var peaks = doc.RootElement.GetProperty("seasonalPeaks").EnumerateArray().ToList();
        Assert.True(peaks.Count >= 2);

        var events = peaks.Select(p => p.GetProperty("event").GetString()).ToList();
        Assert.Contains("monthlyPayroll", events);
    }

    [Fact]
    public void Quarterly_review_schedule_defined()
    {
        var path = RepoPaths.Resolve("docs", "cap", "capacity-forecast.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var schedule = doc.RootElement.GetProperty("reviewSchedule");
        Assert.Equal("quarterly", schedule.GetProperty("frequency").GetString());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "CAP_003_CAPACITY_PLAN.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "cap", "capacity-forecast.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "cap003-validate.ps1");
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
