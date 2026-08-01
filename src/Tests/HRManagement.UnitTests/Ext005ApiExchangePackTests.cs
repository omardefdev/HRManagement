using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Ext005ApiExchangePackTests
{

    [Fact]
    public void Matrix_has_catalog_compat_purpose_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "ext-api", "api-exchange-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("EXT-005", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CatalogPublish", journeys);
        Assert.Contains("ContractOwnerSla", journeys);
        Assert.Contains("BackwardCompatMeasured", journeys);
        Assert.Contains("NoSharedDb", journeys);
        Assert.Contains("PurposeBoundExport", journeys);
        Assert.Contains("ConsumerDrivenBreak", journeys);
        Assert.Contains("SchemaRegistryCompat", journeys);
        Assert.Contains("ThrottleAndCorrelation", journeys);
        Assert.Contains("DeprecationWindow", journeys);
        Assert.Contains("InteropHarness", journeys);

        Assert.True(root.GetProperty("catalogRequired").GetBoolean());
        Assert.True(root.GetProperty("sharedDbForbidden").GetBoolean());
        Assert.True(root.GetProperty("compatibility").GetProperty("supportNMinus1").GetBoolean());
        Assert.True(root.GetProperty("consumerDriven").GetProperty("certifiedConsumersProtected").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveBusForbidden").GetBoolean());

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["IAM-004"]);
        Assert.Equal("Satisfied", blockers["TEN-003"]);
        Assert.Equal("Satisfied", blockers["INT-002"]);
        Assert.Equal("Satisfied", blockers["PROD-007"]);
        Assert.Equal("Blocked", blockers["gov-exchange-bus-live"]);
        Assert.Equal("Blocked", blockers["schema-registry-live"]);
        Assert.Equal("Blocked", blockers["partner-cdc-cert"]);
        Assert.Equal("Blocked", blockers["national-openapi-publish"]);
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var miss = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext005", "scenarios", "missing-owner-sla-fail.json")));
        using var brk = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext005", "scenarios", "breaking-without-bump-fail.json")));
        using var n1 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext005", "scenarios", "n1-compat-pass.json")));
        using var db = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext005", "scenarios", "shared-db-fail.json")));
        using var purp = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext005", "scenarios", "purpose-denied-export-fail.json")));
        using var thr = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext005", "scenarios", "throttle-exceed-pass.json")));
        using var audit = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "ext005", "audit", "exchange-audit-pass.json")));

        Assert.Equal("FAIL", miss.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal(JsonValueKind.Null, miss.RootElement.GetProperty("owner").ValueKind);
        Assert.Equal("FAIL", brk.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(brk.RootElement.GetProperty("breakingChange").GetBoolean());
        Assert.Equal("PASS", n1.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", db.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", purp.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("marketing-enrichment", purp.RootElement.GetProperty("purposeCode").GetString());
        Assert.Equal("PASS", thr.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal(429, thr.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal("PASS", audit.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(audit.RootElement.GetProperty("entries").GetArrayLength() >= 1);
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-005-gov-data-exchange-api-contracts.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_CATALOG_AND_METADATA.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_VERSIONING_AND_COMPAT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_SCHEMA_AND_EVENTS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_PURPOSE_EXPORT_AND_MINIMIZATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_THROTTLE_TENANT_ERRORS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_DEPRECATION_AND_NON_REPUDIATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_MOCK_SANDBOX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "ext005-api-exchange-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext005-api-exchange-validate.ps1");
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
