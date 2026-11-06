using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-66 (S3, D-QA-66-1, AC-QA-66d): the governance checks of the CI run when their inputs change, every night on main and on every
/// manual run; otherwise they are recorded as not run by rule, which the guard and the validator accept for those classes only.
/// </summary>
public sealed class Qa066MetaRuleTests : IDisposable
{
    private const string Meta = "AcQa34PushableBaselineTests";
    private const string MetaToo = "AcQa35RemoteCiTests";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qa066-meta-" + Guid.NewGuid().ToString("N"));

    public Qa066MetaRuleTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private (string Decision, string Output) Rule(string @event, params string[] changed)
    {
        var list = Path.Combine(_dir, "changed.txt");
        System.IO.File.WriteAllLines(list, changed);
        var output = Path.Combine(_dir, "output.txt");
        var wrapper = Path.Combine(_dir, "rule.ps1");
        System.IO.File.WriteAllText(wrapper, $$"""
            param([string]$RepoRoot)
            $env:GITHUB_OUTPUT = '{{output}}'
            $env:GITHUB_STEP_SUMMARY = ''
            & '{{Path.Combine(RepoPaths.Root, "scripts", "ci", "acceptance-meta-rule.ps1")}}' -RepoRoot $RepoRoot -Event '{{@event}}' -ChangedFile '{{list}}'
            exit $LASTEXITCODE
            """);
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(wrapper);
        Assert.True(exit == 0, stdout + stderr);
        return (System.IO.File.ReadAllLines(output).Single(l => l.StartsWith("meta=", StringComparison.Ordinal))[5..], stdout + stderr);
    }

    [Theory]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData("scripts/ci/acceptance-job.ps1")]
    [InlineData("scripts/ci-local.ps1")]
    [InlineData("scripts/plan035-prepush.ps1")]
    [InlineData("tests/acceptance/ci-scope.json")]
    [InlineData("testdata/qa/branch-protection/live-match.json")]
    [InlineData("docs/qa/dated-obligations.json")]
    [InlineData(".gitleaks.toml")]
    [InlineData("src/Tests/HRManagement.AcceptanceTests/AcQa35RemoteCiTests.cs")]
    [InlineData("src/Tests/HRManagement.AcceptanceTests/AcQa34PushableBaselineTests.cs")]
    public void A_pull_request_that_touches_a_trigger_path_runs_them(string path)
    {
        var (decision, output) = Rule("pull_request", "src/Services/HRCore/HR.Core.Web/Program.cs", path);
        Assert.True(decision == "run", output);
    }

    [Theory]
    [InlineData("src/Services/HRCore/HR.Core.Web/Program.cs")]
    [InlineData("reports/plan66-progress.md")]
    [InlineData("acceptance-evidence/AC-01/result.json")]
    [InlineData("docs/CI_AND_TESTING.md")]
    [InlineData("scripts/acceptance-up.ps1")]
    [InlineData("src/Tests/HRManagement.AcceptanceTests/Ac01CentralTenantIsolationTests.cs")]
    public void A_pull_request_that_touches_none_leaves_them_out(string path)
    {
        var (decision, output) = Rule("pull_request", path);
        Assert.True(decision == "skip", output);
    }

    [Theory]
    [InlineData("schedule")]
    [InlineData("workflow_dispatch")]
    [InlineData("push")]
    public void Every_other_event_runs_them(string @event)
    {
        var (decision, output) = Rule(@event, "src/Services/HRCore/HR.Core.Web/Program.cs");
        Assert.True(decision == "run", output);
    }

    [Fact]
    public void A_list_of_changes_that_cannot_be_read_runs_them()
    {
        var wrapper = Path.Combine(_dir, "unreadable.ps1");
        var output = Path.Combine(_dir, "output2.txt");
        System.IO.File.WriteAllText(wrapper, $$"""
            param([string]$RepoRoot)
            $env:GITHUB_OUTPUT = '{{output}}'
            $env:GITHUB_STEP_SUMMARY = ''
            & '{{Path.Combine(RepoPaths.Root, "scripts", "ci", "acceptance-meta-rule.ps1")}}' -RepoRoot $RepoRoot -Event 'pull_request' -ChangedFile '{{Path.Combine(_dir, "nothing.txt")}}'
            exit $LASTEXITCODE
            """);
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(wrapper);
        Assert.True(exit == 0, stdout + stderr);
        Assert.Contains("meta=run", System.IO.File.ReadAllText(output), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_trigger_path_points_at_something_in_the_repository()
    {
        using var scope = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "tests", "acceptance", "ci-scope.json")));
        var paths = scope.RootElement.GetProperty("metaRule").GetProperty("triggerPaths").EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.All(paths, path =>
        {
            var plain = path.EndsWith("/**", StringComparison.Ordinal) ? path[..^3] : path;
            var full = Path.Combine(RepoPaths.Root, plain.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(System.IO.File.Exists(full) || Directory.Exists(full), $"{path} matches nothing");
        });
    }

    // ---------------------------------------------------------------- the guard

    private const string ScopeJson = """
        {
          "order": [
            { "part": "lab", "group": "main", "stack": "lab" },
            { "part": "lab", "group": "meta", "stack": "base", "ciOnly": true }
          ],
          "metaRule": { "stage": "lab:meta", "reason": "why", "triggerPaths": [] },
          "lab": [
            { "class": "RequiredPassingTests", "required": true, "group": "main" },
            { "class": "OptionalFailingTests", "required": false, "group": "main" },
            { "class": "MetaOneTests", "required": true, "group": "meta" }
          ],
          "prodLike": [],
          "excluded": []
        }
        """;

    private (int Exit, string Output) Guard(string extra)
    {
        var scope = Path.Combine(_dir, "scope.json");
        System.IO.File.WriteAllText(scope, ScopeJson);
        var trx = Path.Combine(_dir, "trx");
        Directory.CreateDirectory(trx);
        System.IO.File.Copy(Path.Combine(RepoPaths.Root, "testdata", "acceptance-ci", "passing.trx"), Path.Combine(trx, "passing.trx"), overwrite: true);
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(
            Path.Combine(RepoPaths.Root, "scripts", "ci", "acceptance-guard.ps1"), $"-Scope \"{scope}\" -TrxDir \"{trx}\" -OutDir \"{_dir}\" {extra}");
        return (exit, stdout + stderr);
    }

    [Fact]
    public void A_meta_class_that_did_not_run_fails_the_guard_unless_the_rule_left_it_out()
    {
        var (exit, output) = Guard("");
        Assert.Equal(1, exit);
        Assert.Contains("Required: MetaOneTests did not run", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_guard_accepts_a_meta_class_marked_not_run_by_rule()
    {
        var (exit, output) = Guard("-NotRunByRule MetaOneTests");
        Assert.True(exit == 0, output);
        Assert.Contains("## Not run by rule", System.IO.File.ReadAllText(Path.Combine(_dir, "summary.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_guard_refuses_any_other_class_marked_not_run_by_rule()
    {
        var (exit, output) = Guard("-NotRunByRule MetaOneTests,RequiredPassingTests");
        Assert.Equal(1, exit);
        Assert.Contains("RequiredPassingTests is marked not run by rule, and the rule of the scope does not name it", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_guard_refuses_a_class_marked_not_run_by_rule_that_ran()
    {
        var scope = ScopeJson.Replace("\"group\": \"meta\" }\n", "\"group\": \"meta\" }\n", StringComparison.Ordinal)
            .Replace("{ \"class\": \"RequiredPassingTests\", \"required\": true, \"group\": \"main\" }", "{ \"class\": \"RequiredPassingTests\", \"required\": true, \"group\": \"meta\" }", StringComparison.Ordinal);
        var path = Path.Combine(_dir, "scope2.json");
        System.IO.File.WriteAllText(path, scope);
        var trx = Path.Combine(_dir, "trx2");
        Directory.CreateDirectory(trx);
        System.IO.File.Copy(Path.Combine(RepoPaths.Root, "testdata", "acceptance-ci", "passing.trx"), Path.Combine(trx, "passing.trx"), overwrite: true);
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(
            Path.Combine(RepoPaths.Root, "scripts", "ci", "acceptance-guard.ps1"), $"-Scope \"{path}\" -TrxDir \"{trx}\" -NotRunByRule RequiredPassingTests");
        Assert.Equal(1, exit);
        Assert.Contains("is marked not run by rule, and it ran", stdout + stderr, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the summary and the validator

    private (int Exit, string Output) SummarizeAndValidate(string[] classes)
    {
        var evidence = Path.Combine(_dir, "evidence");
        Directory.CreateDirectory(evidence);
        System.IO.File.WriteAllText(Path.Combine(evidence, "not-run-by-rule.json"), JsonSerializer.Serialize(new
        {
            classes,
            stage = "lab:meta",
            reason = "They read the CI itself.",
            lastNightly = "https://github.com/omardefdev/HRManagement/actions/runs/1"
        }));
        var head = "HEAD";
        var (sumExit, sumOut, sumErr) = RepoScriptRunner.RunScript(
            Path.Combine(RepoPaths.Root, "scripts", "acceptance-evidence-summarize.ps1"), $"-EvidenceDir \"{evidence}\" -Head {head}");
        Assert.True(sumExit == 0, sumOut + sumErr);
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(
            Path.Combine(RepoPaths.Root, "scripts", "acceptance-baseline-validate.ps1"), $"-EvidenceDir \"{evidence}\" -Head {head}");
        return (exit, stdout + stderr);
    }

    [Fact]
    public void The_evidence_says_not_run_by_rule_with_the_reason_and_the_last_nightly()
    {
        var (exit, output) = SummarizeAndValidate([Meta, MetaToo]);
        Assert.True(exit == 0, output);
        var markdown = System.IO.File.ReadAllText(Path.Combine(_dir, "evidence", "summary.md"));
        Assert.Contains("## Not run by rule", markdown, StringComparison.Ordinal);
        Assert.Contains("They read the CI itself.", markdown, StringComparison.Ordinal);
        Assert.Contains("actions/runs/1", markdown, StringComparison.Ordinal);
        using var summary = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(_dir, "evidence", "summary.json")));
        Assert.Equal(2, summary.RootElement.GetProperty("notRunByRule").GetArrayLength());
    }

    [Fact]
    public void The_validator_refuses_any_other_class_as_not_run_by_rule()
    {
        var (exit, output) = SummarizeAndValidate([Meta, "AcHcm29HireContractTests"]);
        Assert.Equal(1, exit);
        Assert.Contains("AcHcm29HireContractTests is recorded as not run by rule", output, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the workflow

    private static string Workflow =>
        System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml")).Replace("\r\n", "\n");

    [Fact]
    public void Main_runs_the_whole_workflow_every_night()
    {
        Assert.Matches(@"schedule:\n\s+- cron: ""\d+ \d+ \* \* \*""", Workflow);
    }

    [Fact]
    public void The_meta_stage_runs_unless_the_rule_says_skip()
    {
        // An empty answer of the reuse check runs the stage: only an explicit skip leaves it out.
        Assert.Contains("(matrix.name != 'meta' || needs.reuse-check.outputs.meta != 'skip')", Workflow, StringComparison.Ordinal);
        Assert.Contains("ACCEPTANCE_META_RULE: ${{ needs.reuse-check.outputs.meta }}", Workflow, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"scripts/ci/acceptance-meta-rule\.ps1"), Workflow);
    }
}
