using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Sov005ThreatModelPackTests
{

    [Fact]
    public void Matrix_has_threat_model_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "sov-threat", "threat-model-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("SOV-005", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("methodology").GetProperty("strideRequired").GetBoolean());
        Assert.True(root.GetProperty("highThreatCoverage").GetProperty("controlRequired").GetBoolean());
        Assert.True(root.GetProperty("changeTrigger").GetProperty("materialChangeUpdateRequired").GetBoolean());
        Assert.True(root.GetProperty("residualRisk").GetProperty("signedRequired").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveIndependentTmReviewForbidden").GetBoolean());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("MethodologyBoundaries", journeys);
        Assert.Contains("HighCoverage", journeys);
        Assert.Contains("ChangeMisuseResidual", journeys);
        Assert.Contains("ClaimGate", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["STR-001"]);
        Assert.Equal("Satisfied", blockers["TEN-001"]);
        Assert.Equal("Satisfied", blockers["INT-003"]);
        Assert.Equal("Satisfied", blockers["SOV-004"]);
        Assert.Equal("Blocked", blockers["live-independent-tm-review"]);
        Assert.Equal("Blocked", blockers["live-attack-tree-walkthrough"]);
        Assert.Equal("Blocked", blockers["live-misuse-backlog-sync"]);
        Assert.Equal("Blocked", blockers["live-residual-risk-signoff"]);
        Assert.Equal("Satisfied", blockers["SOV-006"]);
        Assert.Equal("NotStarted", blockers["NAT-001"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-005-threat-modeling.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_005_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_005_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov005", "scenarios", "missing-stride-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov005", "harness", "threat-model-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "sov005-threat-model-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov005-threat-model-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "sov-threat", "threat-model-matrix.json");
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
