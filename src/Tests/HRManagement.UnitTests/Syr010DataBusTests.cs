using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Syr010DataBusTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr010-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Profile_card_is_syr010()
    {
        var path = RepoPaths.Resolve("docs", "syr", "data-bus-profile.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("SYR-010", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Profile_forbids_live_bus_and_shadow_integrations()
    {
        var path = RepoPaths.Resolve("docs", "syr", "data-bus-profile.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.True(root.GetProperty("liveBusClaimForbidden").GetBoolean());
        Assert.True(root.GetProperty("shadowIntegrationForbidden").GetBoolean());
        Assert.Equal("transitional", root.GetProperty("busStatus").GetString());
    }

    [Fact]
    public void Connectors_have_required_fields()
    {
        var path = RepoPaths.Resolve("docs", "syr", "data-bus-profile.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var connectors = doc.RootElement.GetProperty("connectors").EnumerateArray().ToList();

        Assert.True(connectors.Count >= 2);

        foreach (var conn in connectors)
        {
            Assert.False(string.IsNullOrWhiteSpace(conn.GetProperty("connectorId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(conn.GetProperty("authority").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(conn.GetProperty("classification").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(conn.GetProperty("purpose").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(conn.GetProperty("contractVersion").GetString()));
            Assert.True(conn.TryGetProperty("slo", out _));
        }
    }

    [Fact]
    public void Transitional_batch_policy_requires_manifest_and_replay()
    {
        var path = RepoPaths.Resolve("docs", "syr", "data-bus-profile.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var policy = doc.RootElement.GetProperty("transitionalBatchPolicy");

        Assert.True(policy.GetProperty("manifestRequired").GetBoolean());
        Assert.True(policy.GetProperty("replaySafe").GetBoolean());
        Assert.True(policy.GetProperty("ackRequired").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_010_DATA_BUS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "data-bus-profile.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr010-validate.ps1");
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
