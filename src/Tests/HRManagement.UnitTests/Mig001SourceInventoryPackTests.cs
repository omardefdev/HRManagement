using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Mig001SourceInventoryPackTests
{

    [Fact]
    public void Matrix_has_journeys_sources_sot_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "mig-inventory", "source-inventory.json")));
        var root = doc.RootElement;
        Assert.Equal("MIG-001", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SourceCatalog", journeys);
        Assert.Contains("FieldSot", journeys);
        Assert.Contains("MeasuredVolumeQuality", journeys);
        Assert.Contains("AccessRisks", journeys);
        Assert.Contains("SamplingProfiling", journeys);
        Assert.Contains("CoverageGate", journeys);

        Assert.True(root.GetProperty("sources").GetArrayLength() >= 6);
        var sourceIds = root.GetProperty("sources").EnumerateArray()
            .Select(s => s.GetProperty("sourceId").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var u in root.GetProperty("knownSourceUniverse").EnumerateArray())
            Assert.Contains(u.GetString()!, sourceIds);

        foreach (var s in root.GetProperty("sources").EnumerateArray())
        {
            if (s.GetProperty("status").GetString() != "Inventoried")
                continue;
            Assert.False(s.GetProperty("volume").GetProperty("estimateOnly").GetBoolean());
            Assert.True(s.GetProperty("accessRisks").GetArrayLength() >= 1);
        }

        var sot = root.GetProperty("targetFieldSot").EnumerateArray()
            .Select(t => t.GetProperty("targetField").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in root.GetProperty("requiredTargetFields").EnumerateArray())
            Assert.Contains(need.GetString()!, sot);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["STR-001"]);
        Assert.Equal("Satisfied", blockers["GOV-003"]);
        Assert.Equal("Blocked", blockers["agency-extract-access"]);
        Assert.Equal("Blocked", blockers["SYR-002"]);
    }

    [Fact]
    public void Unsupported_has_signed_exception()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "mig-inventory", "source-inventory.json")));
        var exceptions = doc.RootElement.GetProperty("exceptions").EnumerateArray()
            .Select(e => e.GetProperty("sourceId").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in doc.RootElement.GetProperty("sources").EnumerateArray())
        {
            var status = s.GetProperty("status").GetString();
            if (status is "Unsupported" or "ExceptionSigned")
                Assert.Contains(s.GetProperty("sourceId").GetString()!, exceptions);
        }
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var profile = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig001", "profiling", "agency-signed-profile.json")));
        using var access = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig001", "scenarios", "undocumented-access-risk.json")));
        using var est = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig001", "scenarios", "estimate-only-volume.json")));
        using var sot = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig001", "scenarios", "target-field-no-sot.json")));
        using var cov = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig001", "exceptions", "signed-unsupported.json")));

        Assert.Equal("PASS", profile.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(profile.RootElement.GetProperty("agencySigned").GetBoolean());
        Assert.Equal("FAIL", access.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal(0, access.RootElement.GetProperty("accessRisks").GetArrayLength());
        Assert.Equal("FAIL", est.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(est.RootElement.GetProperty("volume").GetProperty("estimateOnly").GetBoolean());
        Assert.Equal("FAIL", sot.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal(JsonValueKind.Null, sot.RootElement.GetProperty("sourceOfTruthSourceId").ValueKind);
        Assert.Equal("PASS", cov.RootElement.GetProperty("expectedValidate").GetString());
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-001-legacy-source-inventory-field-sot.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_SOURCE_CATALOG.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_FIELD_SOT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_ACCESS_RISKS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_FREEZE_CONSTRAINTS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_UNSUPPORTED_SYSTEMS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_PROFILING_PROTOCOL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "mig001-source-inventory-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig001-source-inventory-validate.ps1");
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
