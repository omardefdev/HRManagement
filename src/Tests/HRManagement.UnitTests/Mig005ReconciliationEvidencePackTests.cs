using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Mig005ReconciliationEvidencePackTests
{

    [Fact]
    public void Matrix_has_buckets_evidence_dual_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "mig-evidence", "recon-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("MIG-005", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("FullAccounted", journeys);
        Assert.Contains("FinancialControls", journeys);
        Assert.Contains("BusinessSamples", journeys);
        Assert.Contains("ImmutableEvidence", journeys);
        Assert.Contains("ChecksumRecon", journeys);
        Assert.Contains("DualControlSignOff", journeys);

        var buckets = root.GetProperty("accountedBuckets").EnumerateArray()
            .Select(b => b.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Migrated", buckets);
        Assert.Contains("Rejected", buckets);
        Assert.Contains("Deferred", buckets);

        Assert.True(root.GetProperty("financialPolicy").GetProperty("unexplainedVarianceForbidden").GetBoolean());
        Assert.True(root.GetProperty("evidencePackage").GetProperty("immutable").GetBoolean());
        Assert.True(root.GetProperty("dualControl").GetProperty("minSigners").GetInt32() >= 2);

        var sig = root.GetProperty("signing");
        Assert.NotEqual(sig.GetProperty("signedByRole").GetString(), sig.GetProperty("reviewerRole").GetString());

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["MIG-003"]);
        Assert.Equal("Satisfied", blockers["OBS-003"]);
        Assert.Equal("Blocked", blockers["agency-extract-access"]);
        Assert.Equal("Blocked", blockers["live-agency-recon"]);
        Assert.Equal("Blocked", blockers["live-independent-sql"]);
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var full = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig005", "scenarios", "full-accounted-pass.json")));
        using var gap = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig005", "scenarios", "unexplained-gap-fail.json")));
        using var fin = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig005", "scenarios", "financial-variance-fail.json")));
        using var dual = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig005", "scenarios", "dual-control-missing-fail.json")));
        using var tamp = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig005", "scenarios", "tamper-evidence-fail.json")));
        using var chk = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig005", "recon", "checksum-recon-pass.json")));
        using var biz = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig005", "samples", "business-sample-signoff.json")));
        using var pkg = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig005", "harness", "evidence-package-stub.json")));

        Assert.Equal("PASS", full.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal(0, full.RootElement.GetProperty("unexplainedGap").GetInt32());
        Assert.Equal("FAIL", gap.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(gap.RootElement.GetProperty("unexplainedGap").GetInt32() > 0);
        Assert.Equal("FAIL", fin.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", dual.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", tamp.RootElement.GetProperty("expectedValidate").GetString());
        Assert.NotEqual(
            tamp.RootElement.GetProperty("declaredContentHash").GetString(),
            tamp.RootElement.GetProperty("recomputedContentHash").GetString());
        Assert.Equal("PASS", chk.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", biz.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(pkg.RootElement.GetProperty("immutable").GetBoolean());
        Assert.True(pkg.RootElement.GetProperty("artifacts").GetArrayLength() >= 8);
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-005-migration-reconciliation-evidence.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_ACCOUNTED_BUCKETS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_FINANCIAL_CONTROLS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_LINEAGE_AND_REJECTS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_EVIDENCE_PACKAGE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_DUAL_CONTROL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_SAMPLING.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_AGENCY_WAVE_SIGNOFF.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "mig005-evidence-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig005-evidence-validate.ps1");
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
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
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
