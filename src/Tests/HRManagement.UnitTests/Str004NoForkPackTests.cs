using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Str004NoForkPackTests
{

    [Fact]
    public void Matrix_has_journeys_kinds_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-product", "nofork-matrix.json")));
        using var req = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str004", "required-surfaces.json")));

        var root = doc.RootElement;
        Assert.Equal("STR-004", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ConfigTaxonomy", journeys);
        Assert.Contains("ExtensionPoints", journeys);
        Assert.Contains("DualAgencySameRelease", journeys);
        Assert.Contains("PromotionRollback", journeys);
        Assert.Contains("RepoNoForkRule", journeys);

        var kinds = root.GetProperty("surfaces").EnumerateArray()
            .Select(s => s.GetProperty("kind").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var k in req.RootElement.GetProperty("requiredKinds").EnumerateArray())
            Assert.Contains(k.GetString()!, kinds);

        Assert.True(root.GetProperty("surfaces").GetArrayLength() >= req.RootElement.GetProperty("minSurfaces").GetInt32());

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["PROD-001"]);
        Assert.Equal("Satisfied", blockers["ONB-001"]);
        Assert.Equal("Satisfied", blockers["STR-002"]);
        Assert.Equal("Satisfied", blockers["TEN-003"]);
    }

    [Fact]
    public void Dual_agency_fixtures_share_release_digest()
    {
        using var matrix = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-product", "nofork-matrix.json")));
        var digest = matrix.RootElement.GetProperty("releaseDigest").GetString();

        using var a = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str004", "agencies", "agency-a.json")));
        using var b = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str004", "agencies", "agency-b.json")));

        Assert.Equal(digest, a.RootElement.GetProperty("releaseDigest").GetString());
        Assert.Equal(digest, b.RootElement.GetProperty("releaseDigest").GetString());
        Assert.NotEqual(a.RootElement.GetProperty("agencyId").GetString(),
            b.RootElement.GetProperty("agencyId").GetString());
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-004-single-product-no-fork.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_CONFIG_TAXONOMY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_EXTENSION_POINTS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_DUAL_AGENCY_WALKTHROUGH.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_CONFIG_PROMOTION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_UPGRADE_REHEARSAL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_REPO_NOFORK_RULE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "str004-nofork-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str004-nofork-validate.ps1");
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
