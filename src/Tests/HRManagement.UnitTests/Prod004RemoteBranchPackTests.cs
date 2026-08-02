using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Prod004RemoteBranchPackTests
{

    [Fact]
    public void Matrix_has_remote_branch_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-branch", "remote-branch-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("PROD-004", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("performanceBudgets").GetProperty("required").GetBoolean());
        Assert.True(root.GetProperty("networkProfile").GetProperty("throttledLabRequired").GetBoolean());
        Assert.True(root.GetProperty("responsivePwa").GetProperty("livePwaForbidden").GetBoolean());
        Assert.True(root.GetProperty("resumableUploads").GetProperty("required").GetBoolean());
        Assert.True(root.GetProperty("offline").GetProperty("encryptionRequired").GetBoolean());
        Assert.True(root.GetProperty("accessibility").GetProperty("wcag22AaRequired").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveRemoteBranchForbidden").GetBoolean());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("LowBandwidthCore", journeys);
        Assert.Contains("InterruptResilience", journeys);
        Assert.Contains("LocaleAndA11y", journeys);
        Assert.Contains("OfflineThreat", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["UX-002"]);
        Assert.Equal("Satisfied", blockers["DATA-001"]);
        Assert.Equal("Satisfied", blockers["FILE-001"]);
        Assert.Equal("Blocked", blockers["live-remote-branch-ux"]);
        Assert.Equal("Blocked", blockers["live-pwa-fleet"]);
        Assert.Equal("Blocked", blockers["live-offline-field"]);
        Assert.Equal("NotStarted", blockers["L10N-001"]);
        Assert.Equal("NotStarted", blockers["MOB-001"]);
        Assert.Equal("Satisfied", blockers["PROD-005"]);
        Assert.Equal("NotStarted", blockers["SYR-013"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-004-remote-branch.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_004_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_004_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod004", "scenarios", "below-budget-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod004", "harness", "branch-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "prod004-remote-branch-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod004-remote-branch-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "prod-branch", "remote-branch-matrix.json");
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
