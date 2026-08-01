using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>ANA-001: tests for national analytics semantic layer definitions and validation.</summary>
public class Ana001AnalyticsTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "ana001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void SemanticLayer_json_is_valid()
    {
        var path = RepoPaths.Resolve("docs", "ana", "semantic-layer.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc.RootElement.GetProperty("measures"));
        Assert.NotNull(doc.RootElement.GetProperty("dimensions"));
        Assert.NotNull(doc.RootElement.GetProperty("snapshot_policy"));
        Assert.NotNull(doc.RootElement.GetProperty("permissions"));
    }

    [Fact]
    public void SemanticLayer_has_required_measures()
    {
        var path = RepoPaths.Resolve("docs", "ana", "semantic-layer.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        var measures = doc.RootElement.GetProperty("measures");
        var codes = new List<string>();
        foreach (var m in measures.EnumerateArray())
        {
            codes.Add(m.GetProperty("code").GetString()!);
        }

        Assert.Contains("headcount", codes);
        Assert.Contains("fte", codes);
        Assert.Contains("vacancy_rate", codes);
        Assert.Contains("payroll_total", codes);
        Assert.Contains("turnover_rate", codes);
        Assert.Contains("avg_tenure", codes);
    }

    [Fact]
    public void SemanticLayer_has_required_dimensions()
    {
        var path = RepoPaths.Resolve("docs", "ana", "semantic-layer.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        var dims = doc.RootElement.GetProperty("dimensions");
        var codes = new List<string>();
        foreach (var d in dims.EnumerateArray())
        {
            codes.Add(d.GetProperty("code").GetString()!);
        }

        Assert.Contains("agency", codes);
        Assert.Contains("governorate", codes);
        Assert.Contains("grade", codes);
        Assert.Contains("legal_regime", codes);
        Assert.Contains("gender", codes);
        Assert.Contains("age_band", codes);
        Assert.Contains("service_band", codes);
    }

    [Fact]
    public void SemanticLayer_national_aggregate_separate()
    {
        var path = RepoPaths.Resolve("docs", "ana", "semantic-layer.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        var perms = doc.RootElement.GetProperty("permissions");
        Assert.True(perms.GetProperty("national_aggregate_separate").GetBoolean());
        Assert.True(perms.GetProperty("tenant_rls").GetBoolean());
    }

    [Fact]
    public void SemanticLayer_measures_use_exact_counting()
    {
        var path = RepoPaths.Resolve("docs", "ana", "semantic-layer.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        var measures = doc.RootElement.GetProperty("measures");
        foreach (var m in measures.EnumerateArray())
        {
            var mType = m.GetProperty("type").GetString()!;
            Assert.DoesNotContain("approximate", mType, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SemanticLayer_snapshot_deterministic()
    {
        var path = RepoPaths.Resolve("docs", "ana", "semantic-layer.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        var policy = doc.RootElement.GetProperty("snapshot_policy");
        Assert.True(policy.GetProperty("rebuild_deterministic").GetBoolean());
        Assert.True(policy.GetProperty("as_of_support").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ANA_001_ANALYTICS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ana", "semantic-layer.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ana001-validate.ps1");
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
