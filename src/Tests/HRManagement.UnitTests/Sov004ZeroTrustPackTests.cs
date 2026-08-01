using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Sov004ZeroTrustPackTests
{

    [Fact]
    public void Matrix_has_zero_trust_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "sov-zt", "zero-trust-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("SOV-004", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("noStandingPrivilege").GetProperty("sharedRootForbidden").GetBoolean());
        Assert.True(root.GetProperty("pamJit").GetProperty("approvalRequired").GetBoolean());
        Assert.True(root.GetProperty("adminWorkstations").GetProperty("ordinaryDeviceForbidden").GetBoolean());
        Assert.True(root.GetProperty("breakGlass").GetProperty("immediateAlertRequired").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("livePamForbidden").GetBoolean());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("StandingJit", journeys);
        Assert.Contains("WorkstationMfa", journeys);
        Assert.Contains("BreakGlassRecert", journeys);
        Assert.Contains("ClaimGate", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["IAM-003"]);
        Assert.Equal("Satisfied", blockers["IAM-004"]);
        Assert.Equal("Satisfied", blockers["SOV-003"]);
        Assert.Equal("Blocked", blockers["live-pam-workers"]);
        Assert.Equal("Blocked", blockers["live-admin-workstation-enforcement"]);
        Assert.Equal("Blocked", blockers["live-break-glass-soc"]);
        Assert.Equal("Blocked", blockers["live-access-recertification"]);
        Assert.Equal("Satisfied", blockers["SOV-005"]);
        Assert.Equal("NotStarted", blockers["NAT-001"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-004-zero-trust.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_004_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_004_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov004", "scenarios", "standing-privilege-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov004", "harness", "zero-trust-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "sov004-zero-trust-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov004-zero-trust-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "sov-zt", "zero-trust-matrix.json");
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
