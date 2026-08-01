using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Mig003MigrationFactoryPackTests
{

    [Fact]
    public void Matrix_has_stages_zones_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "mig-factory", "pipeline-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("MIG-003", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("IdempotentReload", journeys);
        Assert.Contains("MidFailResume", journeys);
        Assert.Contains("RecordLineage", journeys);
        Assert.Contains("NoPiiInLogs", journeys);
        Assert.Contains("CutoverThroughput", journeys);
        Assert.Contains("SecureStagingReview", journeys);

        var stages = root.GetProperty("stages").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("extract", stages);
        Assert.Contains("validate", stages);
        Assert.Contains("transform", stages);
        Assert.Contains("quarantine", stages);
        Assert.Contains("load", stages);
        Assert.Contains("purge", stages);

        Assert.True(root.GetProperty("stagingZones").GetArrayLength() >= 3);
        foreach (var z in root.GetProperty("stagingZones").EnumerateArray())
        {
            Assert.True(z.GetProperty("encryptionAtRest").GetBoolean());
            Assert.True(z.GetProperty("tenantScoped").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(z.GetProperty("purgePolicy").GetString()));
        }

        Assert.True(root.GetProperty("idempotency").GetProperty("duplicateOnReloadForbidden").GetBoolean());
        Assert.Equal("skip-identical", root.GetProperty("idempotency").GetProperty("reloadPolicy").GetString());
        Assert.True(root.GetProperty("checkpoint").GetProperty("resumeFromStage").GetBoolean());

        var lin = root.GetProperty("lineage").GetProperty("perRecordFields").EnumerateArray()
            .Select(x => x.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("sourceId", lin);
        Assert.Contains("sourceRecordKey", lin);
        Assert.Contains("mappingVersion", lin);
        Assert.Contains("disposition", lin);
        Assert.Contains("loadBatchId", lin);
        Assert.Contains("loadedAt", lin);
        Assert.Contains("tenantId", lin);

        foreach (var p in root.GetProperty("pipelines").EnumerateArray())
            Assert.Equal("2026.07.30.1", p.GetProperty("codeMapVersionRef").GetString());

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["MIG-002"]);
        Assert.Equal("Satisfied", blockers["TEN-002"]);
        Assert.Equal("Satisfied", blockers["OPS-005"]);
        Assert.Equal("Blocked", blockers["agency-extract-access"]);
        Assert.Equal("Blocked", blockers["live-cutover-perf"]);
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var reload = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig003", "scenarios", "same-batch-reload.json")));
        using var resume = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig003", "scenarios", "mid-stage-fail-resume.json")));
        using var corrupt = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig003", "scenarios", "corrupt-schema-fail.json")));
        using var budget = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig003", "perf", "cutover-window-budget.json")));
        using var review = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig003", "review", "secure-staging-checklist.json")));
        using var lineage = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig003", "harness", "lineage-sample.json")));
        using var logs = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig003", "harness", "log-redaction-sample.json")));

        Assert.Equal("PASS", reload.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal(0, reload.RootElement.GetProperty("duplicatesCreated").GetInt32());
        Assert.Equal("PASS", resume.RootElement.GetProperty("expectedValidate").GetString());
        Assert.False(resume.RootElement.GetProperty("reExtractedCompletedStages").GetBoolean());
        Assert.Equal("FAIL", corrupt.RootElement.GetProperty("expectedValidate").GetString());
        Assert.False(corrupt.RootElement.GetProperty("silentPartialCommit").GetBoolean());
        Assert.True(corrupt.RootElement.GetProperty("quarantineCreated").GetBoolean());
        Assert.Equal("PASS", budget.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(budget.RootElement.GetProperty("fitsWindow").GetBoolean());
        Assert.False(budget.RootElement.GetProperty("liveProof").GetBoolean());
        Assert.Equal("PASS", review.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(lineage.RootElement.GetProperty("records").GetArrayLength() >= 5);
        Assert.Equal("PASS", logs.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(logs.RootElement.GetProperty("plaintextForbiddenAbsent").GetBoolean());
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-003-secure-reentrant-migration-factory.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_STAGING_ZONES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_IDEMPOTENT_LOAD.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_CHECKPOINT_RESUME.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_RECORD_LINEAGE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_PII_LOGGING.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_SECRETS_AND_PURGE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "mig003-factory-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig003-factory-validate.ps1");
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
