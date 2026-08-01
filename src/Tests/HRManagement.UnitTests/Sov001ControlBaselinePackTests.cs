using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Sov001ControlBaselinePackTests
{

    [Fact]
    public void Matrix_has_control_baseline_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "sov-baseline", "control-baseline-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("SOV-001", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("frameworks").GetProperty("localLawTailoringRequired").GetBoolean());
        Assert.True(root.GetProperty("controlCoverage").GetProperty("fullCoverageRequired").GetBoolean());
        Assert.True(root.GetProperty("ownershipEvidence").GetProperty("ownerRequired").GetBoolean());
        Assert.True(root.GetProperty("poam").GetProperty("datedRemediationRequired").GetBoolean());
        Assert.True(root.GetProperty("independentAssessment").GetProperty("liveAtoForbidden").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveAtoForbidden").GetBoolean());

        var crosswalk = root.GetProperty("frameworks").GetProperty("crosswalk").EnumerateArray()
            .Select(s => s.GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in new[] { "NIST-CSF", "NIST-800-53B", "ISO-27001", "ASVS-5", "SSDF" })
            Assert.Contains(need, crosswalk);

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CrosswalkImpact", journeys);
        Assert.Contains("CoverageOwnership", journeys);
        Assert.Contains("PoamExceptions", journeys);
        Assert.Contains("AtoBoundary", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["STR-002"]);
        Assert.Equal("Satisfied", blockers["SUP-001"]);
        Assert.Equal("Satisfied", blockers["GOV-001"]);
        Assert.Equal("Blocked", blockers["live-independent-ato"]);
        Assert.Equal("Blocked", blockers["continuous-assessment-workers"]);
        Assert.Equal("Blocked", blockers["live-poam-system"]);
        Assert.Equal("NotStarted", blockers["SYR-001"]);
        Assert.Equal("Satisfied", blockers["SOV-002"]);
        Assert.Equal("NotStarted", blockers["NAT-001"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-001-control-baseline.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_001_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_001_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov001", "scenarios", "missing-owner-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov001", "harness", "baseline-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "sov001-control-baseline-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov001-control-baseline-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "sov-baseline", "control-baseline-matrix.json");
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
