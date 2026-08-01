using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Prod001PolicyPlatformPackTests
{

    [Fact]
    public void Matrix_has_policy_platform_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-policy", "policy-platform-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("PROD-001", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("versionedTemplates").GetProperty("required").GetBoolean());
        Assert.True(root.GetProperty("effectiveDating").GetProperty("historicalVersionPinned").GetBoolean());
        Assert.True(root.GetProperty("simulation").GetProperty("syntheticOnly").GetBoolean());
        Assert.True(root.GetProperty("invalidPublishForbidden").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveEngineForbidden").GetBoolean());

        var kinds = root.GetProperty("versionedTemplates").GetProperty("kinds").EnumerateArray()
            .Select(k => k.GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in new[] { "policy", "rule", "workflow", "form" })
            Assert.Contains(need, kinds);

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("VersionedChange", journeys);
        Assert.Contains("HistoricalPin", journeys);
        Assert.Contains("SimulationGate", journeys);
        Assert.Contains("PublishSafety", journeys);
        Assert.Contains("OverrideRollback", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["PAY-002"]);
        Assert.Equal("Satisfied", blockers["GOV-002"]);
        Assert.Equal("Satisfied", blockers["STR-002"]);
        Assert.Equal("Blocked", blockers["live-policy-engine"]);
        Assert.Equal("Blocked", blockers["live-workflow-runtime"]);
        Assert.Equal("Blocked", blockers["live-form-designer"]);
        Assert.Equal("Satisfied", blockers["PROD-002"]);
        Assert.Equal("Satisfied", blockers["ONB-001"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-001-policy-platform.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_001_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_001_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod001", "scenarios", "invalid-publish-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod001", "harness", "policy-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "prod001-policy-platform-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod001-policy-platform-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "prod-policy", "policy-platform-matrix.json");
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{repoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"pack validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
