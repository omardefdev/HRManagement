using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Fin002CostTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "fin002-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Rules_card_is_fin002()
    {
        var path = RepoPaths.Resolve("docs", "fin", "cost-allocation-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("FIN-002", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Allocation_requires_100_percent_coverage()
    {
        var path = RepoPaths.Resolve("docs", "fin", "cost-allocation-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var policy = doc.RootElement.GetProperty("allocationPolicy");

        Assert.Equal(100, policy.GetProperty("requiredCoveragePercent").GetInt32());
    }

    [Fact]
    public void Budgets_have_70_85_100_thresholds()
    {
        var path = RepoPaths.Resolve("docs", "fin", "cost-allocation-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var budgets = doc.RootElement.GetProperty("budgets").EnumerateArray().ToList();

        Assert.NotEmpty(budgets);

        var allThresholds = budgets
            .SelectMany(b => b.GetProperty("alertThresholds").EnumerateArray())
            .Select(t => t.GetProperty("percent").GetInt32())
            .ToHashSet();

        Assert.Contains(70, allThresholds);
        Assert.Contains(85, allThresholds);
        Assert.Contains(100, allThresholds);
    }

    [Fact]
    public void Anomaly_detection_is_daily()
    {
        var path = RepoPaths.Resolve("docs", "fin", "cost-allocation-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var anomaly = doc.RootElement.GetProperty("anomalyDetection");

        Assert.Equal("daily", anomaly.GetProperty("frequency").GetString());
    }

    [Fact]
    public void Optimization_guardrails_preserve_slo_and_dr()
    {
        var path = RepoPaths.Resolve("docs", "fin", "cost-allocation-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var guard = doc.RootElement.GetProperty("optimizationGuardrails");

        Assert.True(guard.GetProperty("sloPreservationRequired").GetBoolean());
        Assert.True(guard.GetProperty("drPreservationRequired").GetBoolean());
        Assert.True(guard.GetProperty("forbiddenOptimizations").GetArrayLength() >= 2);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "FIN_002_COST_ALLOCATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "fin", "cost-allocation-rules.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "fin002-validate.ps1");
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
