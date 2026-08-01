using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Sov006PenRedPackTests
{

    [Fact]
    public void Matrix_has_pen_red_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "sov-pentest", "pen-red-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("SOV-006", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("independence").GetProperty("assessorIndependentRequired").GetBoolean());
        Assert.True(root.GetProperty("findingClosure").GetProperty("criticalHighZeroOpenRequired").GetBoolean());
        Assert.True(root.GetProperty("purpleDetection").GetProperty("socDetectionRequired").GetBoolean());
        Assert.True(root.GetProperty("evidence").GetProperty("signedReportsRequired").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveIndependentPenAssessmentForbidden").GetBoolean());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ScopeIndependence", journeys);
        Assert.Contains("FindingClosure", journeys);
        Assert.Contains("PurpleFraudEvidence", journeys);
        Assert.Contains("ClaimGate", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["SOV-001"]);
        Assert.Equal("Satisfied", blockers["SOV-004"]);
        Assert.Equal("Satisfied", blockers["OBS-003"]);
        Assert.Equal("Satisfied", blockers["REL-001"]);
        Assert.Equal("Satisfied", blockers["SOV-005"]);
        Assert.Equal("Blocked", blockers["live-independent-pen-assessment"]);
        Assert.Equal("Blocked", blockers["live-red-team-exercise"]);
        Assert.Equal("Blocked", blockers["live-purple-detection-validation"]);
        Assert.Equal("Blocked", blockers["live-remediation-retest-closure"]);
        Assert.Equal("NotStarted", blockers["NAT-001"]);
        Assert.Equal("NotStarted", blockers["NAT-002"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-006-pen-red.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_006_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_006_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov006", "scenarios", "open-critical-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov006", "harness", "pen-red-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "sov006-pen-red-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov006-pen-red-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "sov-pentest", "pen-red-matrix.json");
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
