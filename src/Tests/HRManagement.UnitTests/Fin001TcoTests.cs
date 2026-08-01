using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>FIN-001: tests for 5-year TCO model schema and calculations.</summary>
public class Fin001TcoTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "fin001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void TCO_model_covers_5_years()
    {
        var path = RepoPaths.Resolve("docs", "fin", "tco-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("FIN-001", root.GetProperty("card").GetString());

        var years = root.GetProperty("years").EnumerateArray().ToList();
        Assert.Equal(5, years.Count);

        Assert.Equal(1, years[0].GetProperty("year").GetInt32());
        Assert.Equal(5, years[4].GetProperty("year").GetInt32());
    }

    [Fact]
    public void TCO_model_has_cost_categories()
    {
        var path = RepoPaths.Resolve("docs", "fin", "tco-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var categories = doc.RootElement.GetProperty("costCategories")
            .EnumerateArray().ToList();
        Assert.True(categories.Count >= 5);

        var ids = categories.Select(c => c.GetProperty("id").GetString()).ToList();
        Assert.Contains("build", ids);
        Assert.Contains("infra", ids);
        Assert.Contains("licenses", ids);
        Assert.Contains("people", ids);
        Assert.Contains("contingency", ids);
    }

    [Fact]
    public void TCO_model_includes_contingency_reserve()
    {
        var path = RepoPaths.Resolve("docs", "fin", "tco-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var contingency = doc.RootElement.GetProperty("costCategories")
            .EnumerateArray()
            .First(c => c.GetProperty("id").GetString() == "contingency");

        Assert.True(contingency.GetProperty("percentOfTotal").GetInt32() >= 10,
            "Contingency must be at least 10% of total");
    }

    [Fact]
    public void TCO_model_has_unit_metrics()
    {
        var path = RepoPaths.Resolve("docs", "fin", "tco-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var metrics = doc.RootElement.GetProperty("unitMetrics")
            .EnumerateArray().ToList();
        Assert.True(metrics.Count >= 3);

        var ids = metrics.Select(m => m.GetProperty("id").GetString()).ToList();
        Assert.Contains("cost_per_employee_month", ids);
        Assert.Contains("cost_per_payroll_cycle", ids);
    }

    [Fact]
    public void TCO_model_has_sensitivity_analysis()
    {
        var path = RepoPaths.Resolve("docs", "fin", "tco-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var axes = doc.RootElement.GetProperty("sensitivityAxes")
            .EnumerateArray().ToList();
        Assert.True(axes.Count >= 2);

        var demandAxis = axes.First(a => a.GetProperty("axis").GetString() == "demand");
        var variations = demandAxis.GetProperty("variations").EnumerateArray()
            .Select(v => v.GetInt32()).ToList();
        Assert.Contains(-50, variations);
        Assert.Contains(50, variations);
    }

    [Fact]
    public void TCO_model_compares_alternatives()
    {
        var path = RepoPaths.Resolve("docs", "fin", "tco-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var alternatives = doc.RootElement.GetProperty("alternatives")
            .EnumerateArray().ToList();
        Assert.True(alternatives.Count >= 2);
    }

    [Fact]
    public void TCO_model_has_review_schedule()
    {
        var path = RepoPaths.Resolve("docs", "fin", "tco-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var schedule = doc.RootElement.GetProperty("reviewSchedule");
        Assert.Equal("quarterly", schedule.GetProperty("frequency").GetString());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "FIN_001_TCO.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "fin", "tco-model.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "fin001-validate.ps1");
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
