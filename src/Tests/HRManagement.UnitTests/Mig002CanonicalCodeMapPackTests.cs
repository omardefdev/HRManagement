using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Mig002CanonicalCodeMapPackTests
{

    [Fact]
    public void Matrix_has_entities_maps_policies_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "mig-canonical", "code-map-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("MIG-002", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CanonicalModel", journeys);
        Assert.Contains("CodeMapCoverage", journeys);
        Assert.Contains("NoMassOther", journeys);
        Assert.Contains("LegacyIdReference", journeys);
        Assert.Contains("RoundTrip", journeys);
        Assert.Contains("LegalBusinessSignOff", journeys);

        var entities = root.GetProperty("canonicalEntities").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("employee", entities);
        Assert.Contains("org", entities);
        Assert.Contains("job", entities);
        Assert.Contains("payroll", entities);
        Assert.Contains("document", entities);

        Assert.True(root.GetProperty("codeMaps").GetArrayLength() >= 10);
        foreach (var cm in root.GetProperty("codeMaps").EnumerateArray())
        {
            var d = cm.GetProperty("disposition").GetString();
            Assert.Contains(d, new[] { "Map", "Transform", "Quarantine" });
        }

        Assert.True(root.GetProperty("unmappedPolicy").GetProperty("forbidOther").GetBoolean());
        Assert.True(root.GetProperty("legacyIdPolicy").GetProperty("neverUseAsAuthOrTenantKey").GetBoolean());
        Assert.Equal("Asia/Damascus", root.GetProperty("localeCalendar").GetProperty("timezone").GetString());

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["MIG-001"]);
        Assert.Equal("Satisfied", blockers["TEN-001"]);
        Assert.Equal("Satisfied", blockers["GOV-003"]);
        Assert.Equal("Blocked", blockers["agency-extract-access"]);
        Assert.Equal("Blocked", blockers["SYR-003"]);
        Assert.Equal("Blocked", blockers["MDM-001"]);
    }

    [Fact]
    public void Signing_roles_differ_and_hash_present()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "mig-canonical", "code-map-matrix.json")));
        var sig = doc.RootElement.GetProperty("signing");
        Assert.NotEqual(sig.GetProperty("signedByRole").GetString(), sig.GetProperty("reviewerRole").GetString());
        Assert.False(string.IsNullOrWhiteSpace(sig.GetProperty("contentHash").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(sig.GetProperty("mappingVersion").GetString()));
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var cov = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig002", "coverage", "mapping-coverage.json")));
        using var rt = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig002", "roundtrip", "status-org-roundtrip.json")));
        using var mass = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig002", "scenarios", "mass-other-fail.json")));
        using var unmap = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig002", "scenarios", "unmapped-no-quarantine-fail.json")));
        using var auth = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig002", "scenarios", "legacy-id-as-auth-key-fail.json")));
        using var sign = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig002", "signoff", "legal-business-signoff.json")));

        Assert.Equal("PASS", cov.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", rt.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", mass.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(mass.RootElement.GetProperty("otherBucketRate").GetDouble() >
                    mass.RootElement.GetProperty("otherBucketMaxRate").GetDouble());
        Assert.Equal("FAIL", unmap.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal(JsonValueKind.Null, unmap.RootElement.GetProperty("disposition").ValueKind);
        Assert.Equal("FAIL", auth.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("EmpNo", auth.RootElement.GetProperty("proposedAuthKey").GetString());
        Assert.Equal("PASS", sign.RootElement.GetProperty("expectedValidate").GetString());
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-002-canonical-model-code-maps.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_CANONICAL_MODEL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_CODE_MAPS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_UNMAPPED_QUARANTINE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_LEGACY_ID_POLICY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_LOCALE_CALENDAR.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "mig002-code-map-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig002-code-map-validate.ps1");
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
