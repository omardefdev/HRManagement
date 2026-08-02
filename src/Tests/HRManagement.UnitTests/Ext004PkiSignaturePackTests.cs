using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Ext004PkiSignaturePackTests
{

    [Fact]
    public void Matrix_has_signer_ltv_revocation_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "ext-pki", "pki-signature-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("EXT-004", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SignApproval", journeys);
        Assert.Contains("OrgSeal", journeys);
        Assert.Contains("TrustedTimestamp", journeys);
        Assert.Contains("RevocationFailClosed", journeys);
        Assert.Contains("ExpiredCertLtv", journeys);
        Assert.Contains("ClockFailure", journeys);
        Assert.Contains("HsmKeyCustody", journeys);
        Assert.Contains("ArchiveRestoreVerify", journeys);
        Assert.Contains("AlgorithmAgility", journeys);
        Assert.Contains("FormSignatureGate", journeys);

        Assert.True(root.GetProperty("signerIntentRequired").GetBoolean());
        Assert.True(root.GetProperty("signatures").GetProperty("formSignatureNotLegalEvidence").GetBoolean());
        Assert.True(root.GetProperty("revocation").GetProperty("failClosed").GetBoolean());
        Assert.True(root.GetProperty("ltv").GetProperty("requiredAfterExpiry").GetBoolean());
        Assert.True(root.GetProperty("hsmCustody").GetProperty("privateKeyNeverLeavesHsm").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("livePkiForbidden").GetBoolean());

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["STR-002"]);
        Assert.Equal("Satisfied", blockers["OBS-003"]);
        Assert.Equal("Satisfied", blockers["SOV-003"]);
        Assert.Equal("Blocked", blockers["national-pki-live"]);
        Assert.Equal("Blocked", blockers["hsm-prod"]);
        Assert.Equal("Blocked", blockers["tsa-live"]);
        Assert.Equal("Blocked", blockers["SYD-011"]);
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var miss = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext004", "scenarios", "missing-signer-role-fail.json")));
        using var rev = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext004", "scenarios", "revoked-cert-fail.json")));
        using var expFail = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext004", "scenarios", "expired-without-ltv-fail.json")));
        using var expPass = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext004", "scenarios", "expired-cert-ltv-pass.json")));
        using var key = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext004", "scenarios", "key-exfil-fail.json")));
        using var form = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext004", "scenarios", "form-signature-as-legal-fail.json")));
        using var audit = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext004", "audit", "signature-audit-pass.json")));

        Assert.Equal("FAIL", miss.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal(JsonValueKind.Null, miss.RootElement.GetProperty("signerRole").ValueKind);
        Assert.Equal("FAIL", rev.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(rev.RootElement.GetProperty("treatedAsValid").GetBoolean());
        Assert.Equal("FAIL", expFail.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", expPass.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(expPass.RootElement.GetProperty("ltvPackagePresent").GetBoolean());
        Assert.Equal("FAIL", key.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(key.RootElement.GetProperty("privateKeyExported").GetBoolean());
        Assert.Equal("FAIL", form.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", audit.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(audit.RootElement.GetProperty("entries").GetArrayLength() >= 1);
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-004-national-pki-signatures-timestamps.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_SIGNER_INTENT_AND_SEALS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_CERT_VALIDATION_AND_REVOCATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_TRUSTED_TIMESTAMP.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_HSM_AND_KEY_CUSTODY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_LTV_AND_ARCHIVE_EVIDENCE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_ALGORITHM_AGILITY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_MOCK_SANDBOX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "ext004-pki-signature-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext004-pki-signature-validate.ps1");
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
