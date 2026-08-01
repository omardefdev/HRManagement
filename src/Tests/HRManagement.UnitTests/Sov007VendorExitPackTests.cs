using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Sov007VendorExitPackTests
{

    [Fact]
    public void Matrix_has_vendor_exit_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "sov-vendor", "vendor-exit-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("SOV-007", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("ownership").GetProperty("ipSourceBuildIacSbomDocsRequired").GetBoolean());
        Assert.True(root.GetProperty("escrowLicenses").GetProperty("escrowRequired").GetBoolean());
        Assert.True(root.GetProperty("openExport").GetProperty("fullDataExportRequired").GetBoolean());
        Assert.True(root.GetProperty("criticalAlternates").GetProperty("noCriticalWithoutAlternateOrException").GetBoolean());
        Assert.True(root.GetProperty("cleanRoomRebuild").GetProperty("ownedArtifactsOnlyRequired").GetBoolean());
        Assert.True(root.GetProperty("destructionAttestation").GetProperty("priorVendorProofRequired").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveCleanRoomRebuildForbidden").GetBoolean());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("OwnershipEscrow", journeys);
        Assert.Contains("ExportAlternates", journeys);
        Assert.Contains("CleanRoomExit", journeys);
        Assert.Contains("ClaimGate", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["SUP-001"]);
        Assert.Equal("Satisfied", blockers["OPS-005"]);
        Assert.Equal("Satisfied", blockers["DB-001"]);
        Assert.Equal("Satisfied", blockers["SOV-006"]);
        Assert.Equal("Blocked", blockers["live-clean-room-rebuild"]);
        Assert.Equal("Blocked", blockers["live-vendor-exit-drill"]);
        Assert.Equal("Blocked", blockers["live-full-export-import"]);
        Assert.Equal("Blocked", blockers["live-vendor-destruction-attestation"]);
        Assert.Equal("NotStarted", blockers["VEN-001"]);
        Assert.Equal("NotStarted", blockers["NAT-001"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-007-vendor-exit.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_007_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_007_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov007", "scenarios", "rebuild-unowned-artifacts-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov007", "harness", "vendor-exit-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "sov007-vendor-exit-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov007-vendor-exit-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "sov-vendor", "vendor-exit-matrix.json");
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
