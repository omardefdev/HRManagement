using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Prod006SemanticLayerPackTests
{

    [Fact]
    public void Matrix_has_semantic_layer_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-semantic", "semantic-layer-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("PROD-006", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("warehouseDecision").GetProperty("liveWarehouseForbidden").GetBoolean());
        Assert.True(root.GetProperty("semanticMetrics").GetProperty("unifiedDefinitionRequired").GetBoolean());
        Assert.True(root.GetProperty("tenantRegionRollups").GetProperty("crossTenantForbidden").GetBoolean());
        Assert.True(root.GetProperty("privacyThresholds").GetProperty("smallCellSuppressionRequired").GetBoolean());
        Assert.True(root.GetProperty("lineage").GetProperty("versionAsOfSourceRequired").GetBoolean());
        Assert.True(root.GetProperty("governedBi").GetProperty("liveDashboardForbidden").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveBiDashboardForbidden").GetBoolean());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("KpiDefinitions", journeys);
        Assert.Contains("Reconciliation", journeys);
        Assert.Contains("TenantPrivacy", journeys);
        Assert.Contains("Provenance", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["GOV-003"]);
        Assert.Equal("Satisfied", blockers["FUN-006"]);
        Assert.Equal("Satisfied", blockers["PERF-004"]);
        Assert.Equal("Satisfied", blockers["TEN-003"]);
        Assert.Equal("Blocked", blockers["live-warehouse"]);
        Assert.Equal("Blocked", blockers["live-bi-dashboard"]);
        Assert.Equal("Blocked", blockers["live-open-data-feed"]);
        Assert.Equal("NotStarted", blockers["ANA-001"]);
        Assert.Equal("NotStarted", blockers["REP-001"]);
        Assert.Equal("Satisfied", blockers["PROD-007"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-006-semantic-layer.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_006_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_006_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod006", "scenarios", "undefined-kpi-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod006", "harness", "semantic-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "prod006-semantic-layer-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod006-semantic-layer-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "prod-semantic", "semantic-layer-matrix.json");
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
