using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Ext007SectorCatalogPackTests
{

    [Fact]
    public void Matrix_has_sector_catalog_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "ext-sector", "sector-catalog-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("EXT-007", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("noImplicitCriticalIntegrations").GetBoolean());
        Assert.True(root.GetProperty("adapterRequirements").GetProperty("contractRequired").GetBoolean());
        Assert.True(root.GetProperty("notification").GetProperty("sensitivePiiForbidden").GetBoolean());
        Assert.True(root.GetProperty("failure").GetProperty("swallowForbidden").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveAdaptersForbidden").GetBoolean());

        var domains = root.GetProperty("catalogDomains").EnumerateArray()
            .Select(d => d.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in new[] { "pension", "insurance", "tax", "attendance", "erp", "budget", "mail-notify" })
            Assert.Contains(need, domains);

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("NoImplicitCritical", journeys);
        Assert.Contains("AdapterCompleteness", journeys);
        Assert.Contains("NotificationPii", journeys);
        Assert.Contains("FailureSurfacing", journeys);
        Assert.Contains("CertReconReceipts", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["EXT-005"]);
        Assert.Equal("Satisfied", blockers["STR-003"]);
        Assert.Equal("Blocked", blockers["live-sector-adapters"]);
        Assert.Equal("Blocked", blockers["live-notification-channel"]);
        Assert.Equal("Blocked", blockers["adapter-fleet-certified-live"]);
        Assert.Equal("Satisfied", blockers["PROD-005"]);
        Assert.Equal("NotStarted", blockers["COM-001"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-007-sector-catalog.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_007_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_007_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "ext007", "scenarios", "implicit-critical-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "ext007", "harness", "sector-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "ext007-sector-catalog-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext007-sector-catalog-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "ext-sector", "sector-catalog-matrix.json");
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
