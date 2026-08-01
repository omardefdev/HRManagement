using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>MSS-001: tests for manager self-service capability definitions and validation.</summary>
public class Mss001ManagerTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "mss001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void ManagerCapabilities_json_is_valid()
    {
        var path = RepoPaths.Resolve("docs", "mss", "manager-capabilities.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc.RootElement.GetProperty("capabilities"));
        Assert.NotNull(doc.RootElement.GetProperty("conflict_rules"));
        Assert.NotNull(doc.RootElement.GetProperty("delegation_policy"));
    }

    [Fact]
    public void ManagerCapabilities_has_required_capabilities()
    {
        var path = RepoPaths.Resolve("docs", "mss", "manager-capabilities.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        var caps = doc.RootElement.GetProperty("capabilities");
        var codes = new List<string>();
        foreach (var cap in caps.EnumerateArray())
        {
            codes.Add(cap.GetProperty("code").GetString()!);
        }

        Assert.Contains("team.view", codes);
        Assert.Contains("team.calendar", codes);
        Assert.Contains("approval.leave", codes);
        Assert.Contains("approval.overtime", codes);
        Assert.Contains("approval.training", codes);
        Assert.Contains("position.view", codes);
        Assert.Contains("performance.initiate", codes);
        Assert.Contains("performance.submit", codes);
        Assert.Contains("delegation.grant", codes);
        Assert.Contains("dashboard.team", codes);
    }

    [Fact]
    public void ManagerCapabilities_delegation_requires_expiry()
    {
        var path = RepoPaths.Resolve("docs", "mss", "manager-capabilities.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        var policy = doc.RootElement.GetProperty("delegation_policy");
        Assert.True(policy.GetProperty("requires_explicit_expiry").GetBoolean());
        Assert.True(policy.GetProperty("temporary").GetBoolean());
    }

    [Fact]
    public void ManagerCapabilities_conflict_rules_defined()
    {
        var path = RepoPaths.Resolve("docs", "mss", "manager-capabilities.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        var rules = doc.RootElement.GetProperty("conflict_rules");
        Assert.Equal("escalate_to_next_level", rules.GetProperty("action").GetString());

        var triggers = rules.GetProperty("triggers");
        Assert.True(triggers.GetArrayLength() >= 3);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MSS_001_MANAGER_SELF_SERVICE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "mss", "manager-capabilities.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mss001-validate.ps1");
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
