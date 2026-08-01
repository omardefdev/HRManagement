using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Arc001CriticalityPackTests
{

    [Fact]
    public void Matrix_has_criticality_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "arc-criticality", "criticality-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("ARC-001", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("tierClassification").GetProperty("allServicesClassifiedRequired").GetBoolean());
        Assert.True(root.GetProperty("spofPolicy").GetProperty("noUnacceptableSpofInTier01Required").GetBoolean());
        Assert.True(root.GetProperty("numericTargetsTelemetry").GetProperty("everyNumericTargetHasTelemetryRequired").GetBoolean());
        Assert.True(root.GetProperty("recoveryOrder").GetProperty("orderedStepsRequired").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveArchitectureFailureWalkthroughForbidden").GetBoolean());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("TierClassification", journeys);
        Assert.Contains("DependenciesPlatform", journeys);
        Assert.Contains("SpofTelemetryRecovery", journeys);
        Assert.Contains("ClaimGate", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["STR-001"]);
        Assert.Equal("Satisfied", blockers["OBS-001"]);
        Assert.Equal("Satisfied", blockers["OPS-001"]);
        Assert.Equal("Satisfied", blockers["SOV-007"]);
        Assert.Equal("Blocked", blockers["live-architecture-failure-walkthrough"]);
        Assert.Equal("Blocked", blockers["live-controlled-dependency-shutdown"]);
        Assert.Equal("Blocked", blockers["live-recovery-order-drill"]);
        Assert.Equal("Blocked", blockers["live-full-dependency-discovery"]);
        Assert.Equal("Satisfied", blockers["ARC-002"]);
        Assert.Equal("Satisfied", blockers["PHY-001"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-ARC-001-criticality.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ARC_001_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ARC_001_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "arc001", "scenarios", "unclassified-service-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "arc001", "harness", "criticality-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "arc001-criticality-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "arc001-criticality-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "arc-criticality", "criticality-matrix.json");
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
