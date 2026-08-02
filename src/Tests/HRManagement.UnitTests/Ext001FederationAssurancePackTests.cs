using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Ext001FederationAssurancePackTests
{

    [Fact]
    public void Matrix_has_personas_trust_mfa_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "ext-federation", "assurance-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("EXT-001", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("AssuranceMapping", journeys);
        Assert.Contains("FederationInterop", journeys);
        Assert.Contains("RevocationSla", journeys);
        Assert.Contains("NoNameOnlyLink", journeys);
        Assert.Contains("CredentialLoss", journeys);
        Assert.Contains("IdpOutageFailover", journeys);
        Assert.Contains("FraudCases", journeys);

        Assert.True(root.GetProperty("assuranceLevels").GetArrayLength() >= 3);
        Assert.True(root.GetProperty("personas").GetArrayLength() >= 5);
        Assert.True(root.GetProperty("attributeAuthority").GetProperty("noNameOnlyLinking").GetBoolean());
        Assert.True(root.GetProperty("citizenEmployeeSeparation").GetProperty("required").GetBoolean());
        Assert.True(root.GetProperty("revocation").GetProperty("slaMinutes").GetInt32() > 0);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["IAM-003"]);
        Assert.Equal("Satisfied", blockers["IAM-004"]);
        Assert.Equal("Satisfied", blockers["STR-002"]);
        Assert.Equal("Blocked", blockers["IAM-001"]);
        Assert.Equal("Blocked", blockers["national-idp-live"]);
        Assert.Equal("Blocked", blockers["agency-idp-live"]);
        Assert.Equal("Blocked", blockers["phishing-mfa-fleet"]);
        Assert.Equal("Blocked", blockers["live-federation-conformance"]);
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var sign = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext001", "signoff", "assurance-matrix-signoff.json")));
        using var nat = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext001", "interop", "national-idp-interop-pass.json")));
        using var name = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext001", "scenarios", "name-only-link-fail.json")));
        using var revP = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext001", "scenarios", "revocation-sla-pass.json")));
        using var revF = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext001", "scenarios", "revocation-sla-breach-fail.json")));
        using var fraud = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext001", "adversarial", "fraud-cases.json")));

        Assert.Equal("PASS", sign.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", nat.RootElement.GetProperty("expectedValidate").GetString());
        Assert.False(nat.RootElement.GetProperty("live").GetBoolean());
        Assert.Equal("FAIL", name.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FullName", name.RootElement.GetProperty("linkKey").GetString());
        Assert.Equal("PASS", revP.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(revP.RootElement.GetProperty("withinSla").GetBoolean());
        Assert.Equal("FAIL", revF.RootElement.GetProperty("expectedValidate").GetString());
        Assert.False(revF.RootElement.GetProperty("withinSla").GetBoolean());
        Assert.Equal("PASS", fraud.RootElement.GetProperty("expectedValidate").GetString());
        Assert.False(fraud.RootElement.GetProperty("anyAllowWithoutReview").GetBoolean());
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-001-gov-identity-federation-assurance.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_ASSURANCE_LEVELS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_FEDERATION_TRUST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_MFA_POLICY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_REVOCATION_AND_LIFECYCLE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_BREAK_GLASS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_CITIZEN_EMPLOYEE_SEPARATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_ATTRIBUTE_AUTHORITY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "ext001-federation-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext001-federation-validate.ps1");
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
