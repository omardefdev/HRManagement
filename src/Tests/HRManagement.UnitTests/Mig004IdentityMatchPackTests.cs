using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Mig004IdentityMatchPackTests
{

    [Fact]
    public void Matrix_has_strategies_thresholds_privacy_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "mig-match", "match-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("MIG-004", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ThresholdSignOff", journeys);
        Assert.Contains("NoHighRiskAutoMerge", journeys);
        Assert.Contains("ReversibleMerge", journeys);
        Assert.Contains("ReviewPrivacy", journeys);
        Assert.Contains("LabelledMetrics", journeys);
        Assert.Contains("AdversarialIdentity", journeys);

        var strategies = root.GetProperty("matchStrategies").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DeterministicNationalId", strategies);
        Assert.Contains("DeterministicComposite", strategies);
        Assert.Contains("ProbabilisticNameDob", strategies);
        Assert.Contains("ExceptionQueue", strategies);

        var thr = root.GetProperty("thresholds");
        Assert.Equal("Low", thr.GetProperty("autoMergeMaxRisk").GetString());
        Assert.True(thr.GetProperty("highRiskAutoMergeForbidden").GetBoolean());

        Assert.True(root.GetProperty("mergeAudit").GetProperty("reversibleRequired").GetBoolean());
        Assert.True(root.GetProperty("reviewPrivacy").GetProperty("forbidCrossAgencyPii").GetBoolean());

        var sig = root.GetProperty("signing");
        Assert.NotEqual(sig.GetProperty("signedByRole").GetString(), sig.GetProperty("reviewerRole").GetString());
        Assert.False(string.IsNullOrWhiteSpace(sig.GetProperty("contentHash").GetString()));

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["MIG-002"]);
        Assert.Equal("Satisfied", blockers["DATA-002"]);
        Assert.Equal("Satisfied", blockers["GOV-001"]);
        Assert.Equal("Satisfied", blockers["GOV-003-merge"]);
        Assert.Equal("Blocked", blockers["agency-extract-access"]);
        Assert.Equal("Done", blockers["L10N-002"]);
        Assert.Equal("Blocked", blockers["live-labelled-metrics"]);
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var sign = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig004", "signoff", "threshold-signoff.json")));
        using var high = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig004", "scenarios", "high-risk-auto-merge-fail.json")));
        using var round = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig004", "scenarios", "merge-unmerge-roundtrip.json")));
        using var xt = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig004", "scenarios", "cross-tenant-review-fail.json")));
        using var pairs = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig004", "labelled", "identity-pairs.json")));
        using var metrics = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig004", "metrics", "precision-recall-lab.json")));
        using var adv = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig004", "adversarial", "twins-name-variants.json")));

        Assert.Equal("PASS", sign.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", high.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("High", high.RootElement.GetProperty("riskLevel").GetString());
        Assert.Equal("AutoMerge", high.RootElement.GetProperty("proposedOutcome").GetString());
        Assert.Equal("PASS", round.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(round.RootElement.GetProperty("merge").GetProperty("reversible").GetBoolean());
        Assert.Equal("FAIL", xt.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(pairs.RootElement.GetProperty("pairs").GetArrayLength() >= 20);
        Assert.Equal("PASS", metrics.RootElement.GetProperty("expectedValidate").GetString());
        Assert.False(metrics.RootElement.GetProperty("liveAgencyEval").GetBoolean());
        Assert.Equal("PASS", adv.RootElement.GetProperty("expectedValidate").GetString());
        Assert.False(adv.RootElement.GetProperty("anyAutoMerge").GetBoolean());
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-004-identity-match-merge.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_MATCH_STRATEGIES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_THRESHOLDS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_NATIONAL_ID_EXCEPTIONS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_HUMAN_REVIEW.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_MERGE_UNMERGE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_FRAUD_INDICATORS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_REVIEW_PRIVACY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "mig004-match-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig004-match-validate.ps1");
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
