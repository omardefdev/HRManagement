using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>REL-007: tests for national wave rollout criteria artifacts.</summary>
public class Rel007WaveTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "rel007-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Wave_criteria_json_exists_and_is_valid()
    {
        var path = RepoPaths.Resolve("docs", "rel", "wave-entry-exit-criteria.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("REL-007", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Wave_types_include_pilot_sector_and_national()
    {
        var path = RepoPaths.Resolve("docs", "rel", "wave-entry-exit-criteria.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var ids = doc.RootElement.GetProperty("waveTypes").EnumerateArray()
            .Select(w => w.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("pilot-small", ids);
        Assert.Contains("sector-large", ids);
        Assert.Contains("national-distributed", ids);
    }

    [Fact]
    public void Exit_criteria_require_three_payroll_cycles()
    {
        var path = RepoPaths.Resolve("docs", "rel", "wave-entry-exit-criteria.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var exit = doc.RootElement.GetProperty("exitCriteria").EnumerateArray()
            .Select(e => e.GetProperty("criterion").GetString() ?? "")
            .ToList();
        Assert.Contains(exit, c => c.Contains("3", StringComparison.Ordinal) && c.Contains("payroll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hypercare_requires_daily_reconciliation()
    {
        var path = RepoPaths.Resolve("docs", "rel", "wave-entry-exit-criteria.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("hypercare").GetProperty("dailyReconciliation").GetBoolean());
    }

    [Fact]
    public void Rollback_requires_rehearsal()
    {
        var path = RepoPaths.Resolve("docs", "rel", "wave-entry-exit-criteria.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("rollback").GetProperty("rehearsalRequired").GetBoolean());
    }

    [Fact]
    public void Doc_exists_and_addresses_wave_rollout()
    {
        var path = RepoPaths.Resolve("docs", "REL_007_WAVE_ROLLOUT.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("REL-007", content);
        Assert.Contains("entry", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exit", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hypercare", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_007_WAVE_ROLLOUT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "rel", "wave-entry-exit-criteria.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "rel007-validate.ps1");
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
