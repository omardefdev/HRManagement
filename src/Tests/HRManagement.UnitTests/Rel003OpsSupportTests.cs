using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Rel003OpsSupportTests
{
    private static readonly string[] RequiredServiceFields =
    [
        "id", "displayName", "ownerRole", "runbookPath", "escalationPath", "onCallTier", "sloRef"
    ];


    [Fact]
    public void Catalog_has_journeys_and_required_service_fields()
    {
        var path = RepoPaths.Resolve("docs", "rel-ops", "service-catalog.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("REL-003", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ServiceCatalog", journeys);
        Assert.Contains("IncidentChange", journeys);
        Assert.Contains("OnCallGameDay", journeys);
        Assert.Contains("AccessPatchPostmortem", journeys);

        var tiers = root.GetProperty("allowedOnCallTiers").EnumerateArray()
            .Select(x => x.GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var services = root.GetProperty("services").EnumerateArray().ToList();
        Assert.True(services.Count >= 10, "expected broad OBS inventory coverage");

        foreach (var svc in services)
        {
            foreach (var field in RequiredServiceFields)
            {
                Assert.True(svc.TryGetProperty(field, out var prop),
                    $"service {svc.GetProperty("id").GetString()} missing {field}");
                Assert.False(string.IsNullOrWhiteSpace(prop.GetString()),
                    $"service {svc.GetProperty("id").GetString()} empty {field}");
            }
            var tier = svc.GetProperty("onCallTier").GetString()!;
            Assert.True(tiers.Contains(tier), $"invalid onCallTier {tier}");
        }
    }

    [Fact]
    public void Game_day_scenario_and_schema_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "rel003", "game-day", "scenario.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "rel003", "game-day", "mtta-mttr-ledger.schema.json")));
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "rel003", "game-day", "scenario.json")));
        var root = doc.RootElement;
        Assert.Equal("REL-003", root.GetProperty("card").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("timeline").GetProperty("acknowledgedAt").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("timeline").GetProperty("resolvedAt").GetString()));
    }

    [Fact]
    public void Postmortem_actions_csv_has_status_column()
    {
        var text = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "rel-ops", "postmortem-actions-template.csv"));
        var header = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        Assert.Contains("status", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actionId", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("incidentId", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Access_review_csv_has_decision_column()
    {
        var text = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "rel-ops", "access-review-template.csv"));
        var header = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        Assert.Contains("decision", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("principalId", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Severity_doc_lists_sev1_to_sev4()
    {
        var text = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "REL_003_SEVERITY_SLA.md"));
        Assert.Contains("Sev1", text, StringComparison.Ordinal);
        Assert.Contains("Sev2", text, StringComparison.Ordinal);
        Assert.Contains("Sev3", text, StringComparison.Ordinal);
        Assert.Contains("Sev4", text, StringComparison.Ordinal);
        Assert.Contains("MTTA", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MTTR", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-REL-003-ops-support-sm.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_SEVERITY_SLA.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_INCIDENT_PROCESS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_PROBLEM_PROCESS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_CHANGE_PROCESS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_STATUS_MAINTENANCE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_ONCALL_ROSTER.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_ACCESS_REVIEW.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_PATCH_CADENCE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_POSTMORTEM_TEMPLATE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_003_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "rel003-incident-game-day.ps1")));
    }

    [Fact]
    public void Obs_escalation_points_at_rel003()
    {
        var topo = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "OBS_ALERTING_TOPOLOGY.md"));
        Assert.Contains("REL_003_ONCALL_ROSTER", topo, StringComparison.Ordinal);
        var rb = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "OBS_ALERT_RUNBOOKS.md"));
        Assert.Contains("REL_003_INCIDENT_PROCESS", rb, StringComparison.Ordinal);
    }

    [Fact]
    public void Game_day_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "rel003-incident-game-day.ps1");
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
        proc.WaitForExit(120_000);
        Assert.True(proc.ExitCode == 0, $"game day failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.True(System.IO.File.Exists(Path.Combine(repoRoot, "testdata", "rel003", "game-day", "out", "mtta-mttr-ledger.json")));
    }
}
