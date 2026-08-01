using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Str003ModuleCatalogPackTests
{
    private static readonly string[] RequiredFields =
    [
        "moduleId", "displayName", "personas", "capabilityIds", "availability", "serviceTier",
        "ownerRole", "slaRef", "supportTier", "dependsOn", "readinessStatus",
        "uatPackRef", "runbookRef", "sloRef", "opsCatalogId"
    ];


    [Fact]
    public void Catalog_has_journeys_and_fourteen_app_modules()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-modules", "module-catalog.json")));
        using var req = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str003", "required-modules.json")));

        var root = doc.RootElement;
        Assert.Equal("STR-003", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ModuleInventory", journeys);
        Assert.Contains("ReadinessGate", journeys);
        Assert.Contains("AgencyContract", journeys);
        Assert.Contains("FlagExposureAudit", journeys);

        var byId = root.GetProperty("modules").EnumerateArray()
            .ToDictionary(m => m.GetProperty("moduleId").GetString()!, m => m, StringComparer.OrdinalIgnoreCase);

        foreach (var id in req.RootElement.GetProperty("requiredAppModuleIds").EnumerateArray())
            Assert.True(byId.ContainsKey(id.GetString()!), $"missing {id.GetString()}");
        foreach (var id in req.RootElement.GetProperty("requiredPlatformModuleIds").EnumerateArray())
            Assert.True(byId.ContainsKey(id.GetString()!), $"missing platform {id.GetString()}");
    }

    [Fact]
    public void Ready_modules_have_existing_evidence_files()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-modules", "module-catalog.json")));
        var repoRoot = Path.GetDirectoryName(RepoPaths.Resolve("docs", "str-modules", "module-catalog.json"))!;
        repoRoot = Path.GetFullPath(Path.Combine(repoRoot, "..", ".."));

        foreach (var m in doc.RootElement.GetProperty("modules").EnumerateArray())
        {
            if (m.GetProperty("readinessStatus").GetString() != "Ready")
                continue;
            var id = m.GetProperty("moduleId").GetString();
            foreach (var field in new[] { "uatPackRef", "runbookRef", "sloRef" })
            {
                var rel = m.GetProperty(field).GetString()!;
                Assert.False(string.IsNullOrWhiteSpace(rel), $"{id} empty {field}");
                var full = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(System.IO.File.Exists(full), $"{id} missing {field}: {rel}");
            }
        }
    }

    [Fact]
    public void Modules_have_required_fields()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-modules", "module-catalog.json")));
        foreach (var m in doc.RootElement.GetProperty("modules").EnumerateArray())
        {
            var id = m.GetProperty("moduleId").GetString();
            foreach (var field in RequiredFields)
                Assert.True(m.TryGetProperty(field, out _), $"{id} missing {field}");
        }
    }

    [Fact]
    public void Rel001_and_rel003_point_at_str003()
    {
        var flags = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "REL_001_FEATURE_FLAGS.md"));
        Assert.Contains("STR_003_FEATURE_FLAG_GATE", flags, StringComparison.Ordinal);
        var rel3 = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "REL_003_VERIFICATION.md"));
        Assert.Contains("STR-003", rel3, StringComparison.Ordinal);
        Assert.Contains("module-catalog.json", rel3, StringComparison.Ordinal);
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-003-service-module-catalog.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_003_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_003_MODULE_READINESS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_003_AGENCY_CONTRACT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_003_FEATURE_FLAG_GATE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_003_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_003_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_003_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_003_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_003_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "str003-module-catalog-validate.ps1")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "str003", "required-modules.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str003-module-catalog-validate.ps1");
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
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
