using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using HRManagement.TestSupport;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-41 AC-QA-41b/41d/41e/41g: the states of <c>scripts/acceptance-evidence-summarize.ps1</c> (the only generator) and the
/// failures of <c>scripts/acceptance-baseline-validate.ps1</c>, on fixture folders. Runs without the acceptance stack.
/// </summary>
public class AcceptanceEvidenceSummaryTests : IClassFixture<EvidenceFixture>
{
    private readonly EvidenceFixture _fixture;

    public AcceptanceEvidenceSummaryTests(EvidenceFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("AC-T-FRESH", "Pass")]
    [InlineData("AC-T-WEAK", "Pass")]
    [InlineData("AC-T-OLD-TREE", "Stale")]
    [InlineData("AC-T-OLD-IMAGE", "Stale")]
    [InlineData("AC-T-DIRTY-IMAGE", "Stale")]
    [InlineData("AC-T-NO-IMAGES", "Stale")]
    [InlineData("AC-T-MIXED-TREE", "Stale")]
    [InlineData("AC-T-OLD-DATE", "Stale")]
    [InlineData("AC-T-LEGACY", "NotRun")]
    [InlineData("AC-T-NO-FOLDER", "NotRun")]
    [InlineData("AC-T-MISSING-FILE", "Incomplete")]
    [InlineData("AC-T-NO-RESULT", "Incomplete")]
    [InlineData("AC-T-NEGATIVE-FAILED", "Fail")]
    [InlineData("AC-T-BLOCKED", "Blocked")]
    public void Every_folder_gets_the_state_of_the_rules(string ac, string expected) =>
        Assert.Equal(expected, _fixture.Row(ac).GetProperty("status").GetString());

    [Fact]
    public void A_positive_never_hides_a_failed_negative()
    {
        var row = _fixture.Row("AC-T-NEGATIVE-FAILED");
        Assert.Equal(1, row.GetProperty("pass").GetInt32());
        Assert.Equal(1, row.GetProperty("fail").GetInt32());
    }

    [Fact]
    public void Totals_keep_passed_but_not_proving_apart_from_proven()
    {
        var totals = _fixture.Summary.RootElement.GetProperty("totals");
        Assert.Equal(1, totals.GetProperty("proven").GetInt32());
        Assert.Equal(1, totals.GetProperty("passedNotProving").GetInt32());
        Assert.Equal(6, totals.GetProperty("stale").GetInt32());
        Assert.Equal(2, totals.GetProperty("incomplete").GetInt32());
        Assert.Equal(1, totals.GetProperty("fail").GetInt32());
        Assert.Equal(1, totals.GetProperty("blocked").GetInt32());
    }

    [Fact]
    public void The_markdown_has_the_planned_columns_and_no_byte_order_mark()
    {
        var bytes = File.ReadAllBytes(Path.Combine(_fixture.Root, "summary.md"));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        var text = File.ReadAllText(Path.Combine(_fixture.Root, "summary.md"));
        Assert.Contains(
            "| AC | Status | Scenarios (P/F/B) | Contract | Profile | gitSha | Images | Recorded | Source/Run | Freshness | Proves claim | Owner plan |",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Summarizer_keeps_the_scenario_log_of_the_recorder()
    {
        var root = EvidenceFixture.NewRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "summary.md"), "- **AC-T-FRESH**: Pass (`corr`)\n");
            EvidenceFixture.WriteOwnership(root);
            EvidenceFixture.Summarize(root);
            Assert.Contains("- **AC-T-FRESH**: Pass (`corr`)", File.ReadAllText(Path.Combine(root, "summary.md")), StringComparison.Ordinal);
        }
        finally
        {
            EvidenceFixture.Remove(root);
        }
    }

    [Fact]
    public void Validator_accepts_a_summary_that_matches_its_folders() =>
        Assert.Equal(0, _fixture.Validate().ExitCode);

    [Fact]
    public void Validator_refuses_a_local_summary_in_committed_mode()
    {
        var (exitCode, output) = _fixture.Validate("-Committed -SkipRunLookup");
        Assert.Equal(1, exitCode);
        Assert.Contains("source 'local'", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_accepts_a_ci_summary_of_the_current_code_in_committed_mode()
    {
        var root = EvidenceFixture.NewRoot();
        try
        {
            EvidenceFixture.WriteOwnership(root);
            EvidenceFixture.WriteFolder(root, "AC-T-FRESH", ciRunId: "1234");
            EvidenceFixture.Summarize(root, ciRunId: "1234");
            var (exitCode, output) = EvidenceFixture.Validate(root, "-Committed -SkipRunLookup");
            Assert.True(exitCode == 0, output);
        }
        finally
        {
            EvidenceFixture.Remove(root);
        }
    }

    [Fact]
    public void Validator_refuses_a_ci_summary_of_other_code_in_committed_mode()
    {
        var root = EvidenceFixture.NewRoot();
        try
        {
            EvidenceFixture.WriteOwnership(root);
            EvidenceFixture.WriteFolder(root, "AC-T-FRESH", ciRunId: "1234");
            EvidenceFixture.Summarize(root, ciRunId: "1234");
            var (exitCode, output) = EvidenceFixture.Validate(root, "-Committed -SkipRunLookup", new string('c', 64));
            Assert.Equal(1, exitCode);
            Assert.Contains("is for code tree", output, StringComparison.Ordinal);
        }
        finally
        {
            EvidenceFixture.Remove(root);
        }
    }

    [Fact]
    public void Validator_refuses_evidence_without_an_owner()
    {
        var root = EvidenceFixture.NewRoot();
        try
        {
            EvidenceFixture.WriteOwnership(root);
            EvidenceFixture.WriteFolder(root, "AC-T-ORPHAN");
            EvidenceFixture.Summarize(root);
            var (exitCode, output) = EvidenceFixture.Validate(root);
            Assert.Equal(1, exitCode);
            Assert.Contains("AC-T-ORPHAN has evidence but no owner", output, StringComparison.Ordinal);
        }
        finally
        {
            EvidenceFixture.Remove(root);
        }
    }

    [Fact]
    public void Validator_refuses_a_summary_that_counts_an_incomplete_folder_as_a_pass()
    {
        var root = EvidenceFixture.NewRoot();
        try
        {
            EvidenceFixture.WriteOwnership(root);
            EvidenceFixture.WriteFolder(root, "AC-T-FRESH");
            File.Delete(Path.Combine(root, "AC-T-FRESH", "audit.json"));
            EvidenceFixture.Summarize(root);

            var path = Path.Combine(root, "summary.json");
            var summary = JsonNode.Parse(File.ReadAllText(path))!;
            var row = summary["rows"]!.AsArray().First(r => r!["ac"]!.GetValue<string>() == "AC-T-FRESH")!;
            row["status"] = "Pass";
            File.WriteAllText(path, summary.ToJsonString());

            var (exitCode, output) = EvidenceFixture.Validate(root);
            Assert.Equal(1, exitCode);
            Assert.Contains("the summary says Pass, the evidence says Incomplete", output, StringComparison.Ordinal);
        }
        finally
        {
            EvidenceFixture.Remove(root);
        }
    }

    [Fact]
    public void Validator_refuses_a_markdown_that_says_other_totals_than_the_evidence()
    {
        var root = EvidenceFixture.NewRoot();
        try
        {
            EvidenceFixture.WriteOwnership(root);
            EvidenceFixture.WriteFolder(root, "AC-T-WEAK");
            EvidenceFixture.Summarize(root);

            var path = Path.Combine(root, "summary.md");
            File.WriteAllText(path, File.ReadAllText(path).Replace("0 proven", "1 proven", StringComparison.Ordinal));

            var (exitCode, output) = EvidenceFixture.Validate(root);
            Assert.Equal(1, exitCode);
            Assert.Contains("summary.md does not carry the totals", output, StringComparison.Ordinal);
        }
        finally
        {
            EvidenceFixture.Remove(root);
        }
    }

    [Fact]
    public void Validator_refuses_a_total_that_counts_a_weak_pass_as_proven()
    {
        var root = EvidenceFixture.NewRoot();
        try
        {
            EvidenceFixture.WriteOwnership(root);
            EvidenceFixture.WriteFolder(root, "AC-T-WEAK");
            EvidenceFixture.Summarize(root);

            var path = Path.Combine(root, "summary.json");
            var summary = JsonNode.Parse(File.ReadAllText(path))!;
            summary["totals"]!["proven"] = 1;
            File.WriteAllText(path, summary.ToJsonString());

            var (exitCode, output) = EvidenceFixture.Validate(root);
            Assert.Equal(1, exitCode);
            Assert.Contains("Total 'proven'", output, StringComparison.Ordinal);
        }
        finally
        {
            EvidenceFixture.Remove(root);
        }
    }

    [Fact]
    public void The_code_tree_ignores_evidence_reports_and_docs_only()
    {
        var script = Path.Combine(RepoPaths.Root, "scripts", "acceptance-evidence-lib.ps1");
        var source = File.ReadAllText(script);
        Assert.Contains("(acceptance-evidence|reports|docs)/", source, StringComparison.Ordinal);
    }
}

/// <summary>One fixture folder per state, summarized once.</summary>
public sealed class EvidenceFixture : IDisposable
{
    public const string CodeTree = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string SourceSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static readonly string[] ContractFiles =
        ["metadata.json", "steps.md", "before.json", "after.json", "audit.json", "db-checks.json", "timing.json", "result.json"];

    public EvidenceFixture()
    {
        Root = NewRoot();
        WriteOwnership(Root);
        WriteFolder(Root, "AC-T-FRESH");
        WriteFolder(Root, "AC-T-WEAK");
        WriteFolder(Root, "AC-T-OLD-TREE", codeTree: new string('9', 64));
        WriteFolder(Root, "AC-T-OLD-IMAGE", images: new Dictionary<string, string> { ["hr-core-web"] = new('d', 40) });
        WriteFolder(Root, "AC-T-DIRTY-IMAGE", images: new Dictionary<string, string> { ["hr-core-web"] = SourceSha + "-dirty" });
        WriteFolder(Root, "AC-T-NO-IMAGES", images: []);
        WriteFolder(Root, "AC-T-MIXED-TREE", scenarioTree: new string('9', 64));
        WriteFolder(Root, "AC-T-OLD-DATE", recordedAt: DateTime.UtcNow.AddDays(-31));
        WriteFolder(Root, "AC-T-LEGACY");
        WriteFolder(Root, "AC-T-MISSING-FILE");
        File.Delete(Path.Combine(Root, "AC-T-MISSING-FILE", "audit.json"));
        WriteFolder(Root, "AC-T-NO-RESULT");
        File.Delete(Path.Combine(Root, "AC-T-NO-RESULT", "result.json"));
        WriteFolder(Root, "AC-T-NEGATIVE-FAILED", scenarios: new Dictionary<string, string> { ["positive"] = "Pass", ["negative"] = "Fail" });
        WriteFolder(Root, "AC-T-BLOCKED", scenarios: new Dictionary<string, string> { ["only"] = "Blocked" });
        Summarize(Root);
        Summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "summary.json")));
    }

    public string Root { get; }

    public JsonDocument Summary { get; }

    public JsonElement Row(string ac) =>
        Summary.RootElement.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("ac").GetString() == ac);

    public (int ExitCode, string Output) Validate(string extra = "") => Validate(Root, extra);

    public static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "hr-acceptance-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    public static void Remove(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        File.Delete(OwnershipPath(root));
    }

    public static void WriteOwnership(string root)
    {
        var acs = new JsonObject();
        foreach (var number in Enumerable.Range(1, 37))
            acs[$"AC-{number:D2}"] = Owner(true, false);
        foreach (var ac in new[]
                 {
                     "AC-T-FRESH", "AC-T-OLD-TREE", "AC-T-OLD-IMAGE", "AC-T-DIRTY-IMAGE", "AC-T-NO-IMAGES", "AC-T-MIXED-TREE",
                     "AC-T-OLD-DATE", "AC-T-NO-FOLDER", "AC-T-MISSING-FILE", "AC-T-NO-RESULT", "AC-T-NEGATIVE-FAILED", "AC-T-BLOCKED"
                 })
            acs[ac] = Owner(true, false);
        acs["AC-T-WEAK"] = Owner(false, false);
        acs["AC-T-LEGACY"] = Owner(false, true);
        File.WriteAllText(OwnershipPath(root), new JsonObject { ["acs"] = acs }.ToJsonString());
    }

    public static void WriteFolder(
        string root,
        string ac,
        string codeTree = CodeTree,
        Dictionary<string, string>? images = null,
        string? scenarioTree = null,
        DateTime? recordedAt = null,
        Dictionary<string, string>? scenarios = null,
        string? ciRunId = null)
    {
        var folder = Path.Combine(root, ac);
        Directory.CreateDirectory(folder);
        foreach (var file in ContractFiles)
            File.WriteAllText(Path.Combine(folder, file), "{}");

        var at = (recordedAt ?? DateTime.UtcNow).ToString("o", CultureInfo.InvariantCulture);
        var revisions = new JsonObject();
        foreach (var image in images ?? new Dictionary<string, string> { ["hr-core-web"] = SourceSha, ["hr-gateway"] = SourceSha })
            revisions[image.Key] = image.Value;
        File.WriteAllText(Path.Combine(folder, "metadata.json"), new JsonObject
        {
            ["capturedAtUtc"] = at,
            ["profile"] = "lab",
            ["sourceSha"] = SourceSha,
            ["codeTree"] = codeTree,
            ["imageRevisions"] = revisions,
            ["source"] = ciRunId is null ? "local" : "ci",
            ["ciRunId"] = ciRunId
        }.ToJsonString());

        var map = new JsonObject();
        foreach (var scenario in scenarios ?? new Dictionary<string, string> { ["only"] = "Pass" })
        {
            map[scenario.Key] = new JsonObject
            {
                ["status"] = scenario.Value,
                ["recordedAtUtc"] = at,
                ["codeTree"] = scenarioTree ?? codeTree
            };
        }

        File.WriteAllText(Path.Combine(folder, "result.json"), new JsonObject
        {
            ["scenarios"] = map,
            ["status"] = EvidenceRecorder.AggregateStatus((scenarios ?? new Dictionary<string, string> { ["only"] = "Pass" }).Values),
            ["recordedAtUtc"] = at
        }.ToJsonString());
    }

    /// <summary>Runs the script as a run of that CI id, or as a local run: the runner's own variables never leak in.</summary>
    public static void Summarize(string root, string? ciRunId = null)
    {
        var saved = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        Environment.SetEnvironmentVariable("GITHUB_RUN_ID", ciRunId);
        try
        {
            var script = Path.Combine(RepoPaths.Root, "scripts", "acceptance-evidence-summarize.ps1");
            var (exitCode, stdout, stderr) = RepoScriptRunner.RunScript(
                script, $"-EvidenceDir \"{root}\" -Ownership \"{OwnershipPath(root)}\" -CodeTree {CodeTree}");
            Assert.True(exitCode == 0, stdout + stderr);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_RUN_ID", saved);
        }
    }

    public static (int ExitCode, string Output) Validate(string root, string extra = "", string codeTree = CodeTree)
    {
        var script = Path.Combine(RepoPaths.Root, "scripts", "acceptance-baseline-validate.ps1");
        var (exitCode, stdout, stderr) = RepoScriptRunner.RunScript(
            script, $"-EvidenceDir \"{root}\" -Ownership \"{OwnershipPath(root)}\" -CodeTree {codeTree} {extra}");
        return (exitCode, stdout + stderr);
    }

    public void Dispose()
    {
        Summary.Dispose();
        Remove(Root);
    }

    private static string OwnershipPath(string root) => Path.Combine(root, "..", Path.GetFileName(root) + "-ownership.json");

    private static JsonObject Owner(bool proves, bool legacy) => new()
    {
        ["ownerPlan"] = "PLAN-41",
        ["provesClaim"] = proves,
        ["reason"] = "fixture",
        ["legacyContractOnly"] = legacy
    };
}
