using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Arc002ReferencePackTests
{

    [Fact]
    public void Matrix_has_reference_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "arc-reference", "reference-architecture-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("ARC-002", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("boundedContexts").GetProperty("noUnjustifiedDistributedComplexityRequired").GetBoolean());
        Assert.True(root.GetProperty("multiSiteProfiles").GetProperty("sameArtifactPrimaryAndDrRequired").GetBoolean());
        Assert.True(root.GetProperty("supportedTopologies").GetProperty("unsupportedTopologyRejectedRequired").GetBoolean());
        Assert.True(root.GetProperty("couplingGuardrails").GetProperty("noDirectDbCrossBoundaryRequired").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveTwoSiteDeploymentForbidden").GetBoolean());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ContextsOwnership", journeys);
        Assert.Contains("MultiSiteTopology", journeys);
        Assert.Contains("CouplingFitness", journeys);
        Assert.Contains("ClaimGate", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["ARC-001"]);
        Assert.Equal("Satisfied", blockers["TEN-001"]);
        Assert.Equal("Satisfied", blockers["INT-001"]);
        Assert.Equal("Satisfied", blockers["OPS-005"]);
        Assert.Equal("Blocked", blockers["live-two-site-deployment"]);
        Assert.Equal("Blocked", blockers["live-architecture-fitness-envelope"]);
        Assert.Equal("Blocked", blockers["live-coupling-boundary-scan"]);
        Assert.Equal("Blocked", blockers["live-reference-implementation-signoff"]);
        Assert.Equal("Satisfied", blockers["QA-005"]);
        Assert.Equal("Satisfied", blockers["PHY-001"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-ARC-002-reference-architecture.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ARC_002_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ARC_002_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "arc002", "scenarios", "unjustified-distribution-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "arc002", "harness", "reference-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "arc002-reference-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "arc002-reference-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "arc-reference", "reference-architecture-matrix.json");
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
