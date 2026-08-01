using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Str007ValueKpiPackTests
{

    [Fact]
    public void Matrix_has_journeys_kpis_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-value", "kpi-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("STR-007", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("KpiCatalog", journeys);
        Assert.Contains("BaselineFreeze", journeys);
        Assert.Contains("IndependentMeasure", journeys);
        Assert.Contains("SampleReconcile", journeys);
        Assert.Contains("QuarterlyReview", journeys);
        Assert.Contains("PrivacyGate", journeys);

        Assert.True(root.GetProperty("kpis").GetArrayLength() >= 9);
        foreach (var k in root.GetProperty("kpis").EnumerateArray())
        {
            Assert.NotEqual(
                k.GetProperty("ownerRole").GetString(),
                k.GetProperty("auditorRole").GetString());
            Assert.True(k.GetProperty("antiGamingRules").GetArrayLength() >= 1);
        }

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["STR-001"]);
        Assert.Equal("Satisfied", blockers["STR-003"]);
        Assert.Equal("Satisfied", blockers["GOV-003"]);
        Assert.Equal("NotStarted", blockers["FIN-001"]);
        Assert.Equal("Satisfied", blockers["PROD-003"]);

        Assert.True(root.GetProperty("privacyRules").GetProperty("tenantScoped").GetBoolean());
        Assert.Contains("screenCount", root.GetProperty("forbiddenBenefitMeasures").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void Baselines_frozen_for_required_kpis()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-value", "kpi-matrix.json")));
        var baselines = doc.RootElement.GetProperty("baselines").EnumerateArray()
            .ToDictionary(b => b.GetProperty("kpiId").GetString()!, b => b, StringComparer.OrdinalIgnoreCase);
        foreach (var k in doc.RootElement.GetProperty("kpis").EnumerateArray())
        {
            if (!k.GetProperty("baselineRequired").GetBoolean())
                continue;
            var id = k.GetProperty("id").GetString()!;
            Assert.True(baselines.ContainsKey(id), $"missing baseline for {id}");
            Assert.True(baselines[id].GetProperty("prePilotFreeze").GetBoolean());
        }
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var indep = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str007", "scenarios", "independent-measurement.json")));
        using var pass = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str007", "scenarios", "sample-reconciliation-pass.json")));
        using var fail = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str007", "scenarios", "sample-reconciliation-fail.json")));
        using var screen = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str007", "scenarios", "screen-count-banned.json")));
        using var leak = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str007", "privacy", "cross-tenant-leak.json")));
        using var quarter = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str007", "scenarios", "quarterly-benefit-review.json")));

        Assert.Equal("PASS", indep.RootElement.GetProperty("expectedValidate").GetString());
        Assert.NotEqual(
            indep.RootElement.GetProperty("ownerRole").GetString(),
            indep.RootElement.GetProperty("assessorRole").GetString());
        Assert.Equal("PASS", pass.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", fail.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", screen.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("screenCount", screen.RootElement.GetProperty("claimedBenefitMeasure").GetString());
        Assert.Equal("FAIL", leak.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", quarter.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal(0, quarter.RootElement.GetProperty("forbiddenMeasuresUsed").GetArrayLength());
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-007-value-competition-baseline.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_KPI_DEFINITIONS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_BASELINE_PROTOCOL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_ANTI_GAMING.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_BENEFIT_VS_SCREEN_COUNT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_DASHBOARD_TENANT_PRIVACY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_INDEPENDENT_MEASUREMENT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_SAMPLE_RECONCILIATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_QUARTERLY_BENEFIT_REVIEW.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "str007-value-kpi-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str007-value-kpi-validate.ps1");
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
