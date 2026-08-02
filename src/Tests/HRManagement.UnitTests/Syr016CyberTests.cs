using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>SYR-016: tests for Syrian cybersecurity crosswalk artifacts.</summary>
public class Syr016CyberTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr016-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Crosswalk_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "syr", "cyber-control-crosswalk.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("SYR-016", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Crosswalk_has_legal_framework()
    {
        var path = RepoPaths.Resolve("docs", "syr", "cyber-control-crosswalk.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("legalFramework").GetArrayLength() >= 4);
    }

    [Fact]
    public void Crosswalk_has_controls_with_no_critical_gaps()
    {
        var path = RepoPaths.Resolve("docs", "syr", "cyber-control-crosswalk.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var controls = doc.RootElement.GetProperty("controls").EnumerateArray().ToList();
        Assert.True(controls.Count >= 10);
        foreach (var ctrl in controls)
        {
            Assert.False(string.IsNullOrWhiteSpace(ctrl.GetProperty("articleRef").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(ctrl.GetProperty("implementation").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(ctrl.GetProperty("evidence").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(ctrl.GetProperty("owner").GetString()));
            Assert.NotEqual("critical", ctrl.GetProperty("gapSeverity").GetString(), StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Crosswalk_unifies_incident_clock_with_privacy()
    {
        var path = RepoPaths.Resolve("docs", "syr", "cyber-control-crosswalk.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var incident = doc.RootElement.GetProperty("incidentResponse");
        Assert.True(incident.GetProperty("unifiedClockWithPrivacy").GetBoolean());
    }

    [Fact]
    public void Doc_exists_and_references_law_20()
    {
        var path = RepoPaths.Resolve("docs", "SYR_016_CYBERSECURITY.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("SYR-016", content);
        Assert.Contains("Law 20", content);
        Assert.Contains("NAITS", content);
        Assert.Contains("playbook", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_016_CYBERSECURITY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "cyber-control-crosswalk.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr016-validate.ps1");
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
