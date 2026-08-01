using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Ext003TreasuryPaymentPackTests
{

    [Fact]
    public void Matrix_has_binding_exactly_once_recon_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "ext-treasury", "treasury-payment-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("EXT-003", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ApprovedRunBinding", journeys);
        Assert.Contains("ExactlyOnce", journeys);
        Assert.Contains("TotalsRecon", journeys);
        Assert.Contains("RejectCorrection", journeys);
        Assert.Contains("ManualFileGate", journeys);
        Assert.Contains("CutoffContingency", journeys);
        Assert.Contains("ParallelPayroll", journeys);
        Assert.Contains("OutOfOrderAck", journeys);

        Assert.True(root.GetProperty("approvedRunRequired").GetBoolean());
        Assert.True(root.GetProperty("exactlyOnce").GetProperty("idempotencyKeyRequired").GetBoolean());
        Assert.True(root.GetProperty("reconciliation").GetProperty("totalsMustMatch").GetBoolean());
        Assert.True(root.GetProperty("manualFiles").GetProperty("unauditedForbidden").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveTreasuryForbidden").GetBoolean());

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["PAY-003"]);
        Assert.Equal("Satisfied", blockers["IAM-003"]);
        Assert.Equal("Satisfied", blockers["INT-003"]);
        Assert.Equal("NotStarted", blockers["EXT-004"]);
        Assert.Equal("Blocked", blockers["treasury-live"]);
        Assert.Equal("Blocked", blockers["bank-sandbox-cert"]);
        Assert.Equal("Blocked", blockers["postal-channel-live"]);
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var unapp = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext003", "scenarios", "unapproved-run-fail.json")));
        using var dup = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext003", "scenarios", "duplicate-submit-fail.json")));
        using var tot = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext003", "scenarios", "totals-mismatch-fail.json")));
        using var recon = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext003", "scenarios", "recon-totals-pass.json")));
        using var part = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext003", "scenarios", "partial-reject-correct-pass.json")));
        using var manual = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext003", "scenarios", "manual-file-unaudited-fail.json")));
        using var audit = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext003", "audit", "payment-order-audit-pass.json")));

        Assert.Equal("FAIL", unapp.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("Draft", unapp.RootElement.GetProperty("runStatus").GetString());
        Assert.Equal("FAIL", dup.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(dup.RootElement.GetProperty("secondBusinessEffect").GetBoolean());
        Assert.Equal("FAIL", tot.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", recon.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", part.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(part.RootElement.GetProperty("correctionOpened").GetBoolean());
        Assert.Equal("FAIL", manual.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", audit.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(audit.RootElement.GetProperty("entries").GetArrayLength() >= 1);
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-003-treasury-payment-reconciliation.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_PAYMENT_ORDER_BINDING.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_SIGNATURES_AND_CHANNEL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_ACK_REJECT_RETURN.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_BANK_VERIFY_AND_SETTLEMENT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_RECONCILIATION_AND_IDEMPOTENCY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_CUTOFF_AND_CONTINGENCY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_MOCK_SANDBOX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "ext003-treasury-payment-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext003-treasury-payment-validate.ps1");
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
