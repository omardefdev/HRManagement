using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Ext002CivilRegistryPackTests
{

    [Fact]
    public void Matrix_has_purpose_minimal_audit_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "ext-civil", "civil-registry-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("EXT-002", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PurposeConsent", journeys);
        Assert.Contains("MinimalData", journeys);
        Assert.Contains("MismatchHuman", journeys);
        Assert.Contains("UnavailableMode", journeys);
        Assert.Contains("RateLimitAbuse", journeys);
        Assert.Contains("AuditTrail", journeys);
        Assert.Contains("StaleCorrection", journeys);

        Assert.True(root.GetProperty("consentRequired").GetBoolean());
        Assert.True(root.GetProperty("purposeAllowlist").GetArrayLength() >= 3);
        Assert.True(root.GetProperty("rateLimits").GetProperty("bulkHarvestForbidden").GetBoolean());
        Assert.True(root.GetProperty("unavailableMode").GetProperty("forbidUnverifiedAsTruth").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveMoiForbidden").GetBoolean());

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["EXT-001"]);
        Assert.Equal("Satisfied", blockers["GOV-001"]);
        Assert.Equal("Satisfied", blockers["MIG-004"]);
        Assert.Equal("Blocked", blockers["moi-civil-registry-live"]);
        Assert.Equal("Blocked", blockers["live-sandbox-cert"]);
        Assert.Equal("Done", blockers["L10N-002"]);
        Assert.Equal("Blocked", blockers["national-idp-live"]);
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var purp = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext002", "scenarios", "purpose-mismatch-fail.json")));
        using var excess = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext002", "scenarios", "excess-field-store-fail.json")));
        using var mis = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext002", "scenarios", "mismatch-review-pass.json")));
        using var outT = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext002", "scenarios", "outage-as-truth-fail.json")));
        using var bulk = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext002", "scenarios", "bulk-harvest-fail.json")));
        using var audit = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext002", "audit", "query-audit-pass.json")));

        Assert.Equal("FAIL", purp.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", excess.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", mis.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(mis.RootElement.GetProperty("humanReviewQueued").GetBoolean());
        Assert.Equal("FAIL", outT.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(outT.RootElement.GetProperty("treatedAsVerifiedMatch").GetBoolean());
        Assert.Equal("FAIL", bulk.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", audit.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(audit.RootElement.GetProperty("entries").GetArrayLength() >= 1);
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-002-civil-registry-person-proofing.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_PURPOSE_AND_CONSENT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_MINIMAL_QUERY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_MATCH_AND_MISMATCH.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_CACHE_AND_CORRECTIONS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_UNAVAILABLE_MODE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_RATE_LIMIT_AND_AUDIT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_MOCK_SANDBOX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "ext002-civil-registry-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext002-civil-registry-validate.ps1");
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
