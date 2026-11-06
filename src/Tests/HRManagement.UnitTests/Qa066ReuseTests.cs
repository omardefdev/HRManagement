using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-66 (S2, AC-QA-66c): a run reuses a green full run only for the same pull request, the same repository, the same code tree and
/// the same inputs of the governance checks; every other case, and any error, runs everything. GitHub is replaced by fixture files.
/// </summary>
public sealed class Qa066ReuseTests : IDisposable
{
    private const string Repo = "omardefdev/HRManagement";
    private const string Tree = "aaaa";
    private const string Inputs = "bbbb";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qa066-reuse-" + Guid.NewGuid().ToString("N"));
    private readonly List<object> _runs = [];

    public Qa066ReuseTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private void Write(string name, object value) =>
        System.IO.File.WriteAllText(Path.Combine(_dir, name), JsonSerializer.Serialize(value));

    private static object RunOf(int id, string conclusion = "success", string status = "completed", string @event = "pull_request",
        string headRepository = Repo, int pullRequest = 66, string createdAt = "2026-11-06T10:00:00Z") => new
        {
            id,
            status,
            conclusion,
            @event,
            head_repository = new { full_name = headRepository },
            pull_requests = pullRequest == 0 ? Array.Empty<object>() : [new { number = pullRequest }],
            created_at = createdAt
        };

    /// <summary>A run of the fixture: its entry in the list, its jobs, its identity and its evidence artifact.</summary>
    private void Run(int id, object? run = null, string[]? jobConclusions = null, string tree = Tree, string inputs = Inputs,
        string scope = "full", string reusedFrom = "", string repository = Repo, bool evidence = true, bool expired = false, bool listed = true, bool identity = true, string identityPullRequest = "66")
    {
        run ??= RunOf(id);
        if (listed)
            _runs.Add(run);
        Write($"run-{id}.json", run);
        Write($"jobs-{id}.json", new { jobs = (jobConclusions ?? ["success", "success"]).Select((c, i) => new { name = $"job{i}", conclusion = c }) });
        if (identity)
            Write($"identity-{id}.json", new { repository, pullRequest = identityPullRequest, codeTree = tree, outsideTree = inputs, scope, reusedFrom });
        Write($"artifacts-{id}.json", new { artifacts = evidence ? new object[] { new { name = "acceptance-evidence", expired } } : [] });
    }

    private (string Reuse, string ReusedRun, string Output, JsonDocument Identity) Decide(string @event = "pull_request", string headRepository = Repo,
        string pullRequest = "66", string classes = "", string simulate = "", string tree = Tree, string inputs = Inputs)
    {
        Write("runs.json", new { workflow_runs = _runs });
        var identityFile = Path.Combine(_dir, "out-identity.json");
        var output = Path.Combine(_dir, "output.txt");
        var wrapper = Path.Combine(_dir, "run.ps1");
        // Through a wrapper: GITHUB_OUTPUT is an environment variable, and an empty argument does not survive powershell -File.
        System.IO.File.WriteAllText(wrapper, $$"""
            param([string]$RepoRoot)
            $env:GITHUB_OUTPUT = '{{output}}'
            $env:GITHUB_STEP_SUMMARY = ''
            & '{{Path.Combine(RepoPaths.Root, "scripts", "ci", "acceptance-reuse.ps1")}}' -RepoRoot $RepoRoot -Event '{{@event}}' -Repository '{{Repo}}' -RunId '999' -Sha 'merge' `
                -PullRequest '{{pullRequest}}' -HeadRepository '{{headRepository}}' -HeadSha 'head' -HeadRef 'branch' -Classes '{{classes}}' -SimulateMissing '{{simulate}}' `
                -CodeTree '{{tree}}' -OutsideTree '{{inputs}}' -FixtureDir '{{_dir}}' -IdentityFile '{{identityFile}}'
            exit $LASTEXITCODE
            """);
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(wrapper);
        Assert.True(exit == 0, stdout + stderr);
        var lines = System.IO.File.ReadAllLines(output);
        string Value(string key) => lines.Single(l => l.StartsWith(key + "=", StringComparison.Ordinal))[(key.Length + 1)..];
        return (Value("reuse"), Value("reused-run"), stdout + stderr, JsonDocument.Parse(System.IO.File.ReadAllText(identityFile)));
    }

    private void AssertNoReuse(string why, string @event = "pull_request", string headRepository = Repo, string pullRequest = "66", string classes = "", string simulate = "", string tree = Tree, string inputs = Inputs)
    {
        var (reuse, run, output, identity) = Decide(@event, headRepository, pullRequest, classes, simulate, tree, inputs);
        identity.Dispose();
        Assert.Equal("false", reuse);
        Assert.Equal("", run);
        Assert.Contains(why, output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_tree_of_a_green_full_run_of_the_pull_request_is_reused()
    {
        Run(100);
        var (reuse, run, output, identity) = Decide();
        using (identity)
        {
            Assert.True(reuse == "true", output);
            Assert.Equal("100", run);
            Assert.Equal("100", identity.RootElement.GetProperty("reusedFrom").GetString());
            Assert.Equal("full", identity.RootElement.GetProperty("scope").GetString());
            Assert.Equal(Tree, identity.RootElement.GetProperty("codeTree").GetString());
            Assert.Equal("merge", identity.RootElement.GetProperty("headSha").GetString());
        }
    }

    [Fact]
    public void A_chain_of_reuses_points_at_the_original_run()
    {
        Run(100);
        Run(101, RunOf(101, createdAt: "2026-11-06T11:00:00Z"), reusedFrom: "100");
        var (reuse, run, output, identity) = Decide();
        identity.Dispose();
        Assert.True(reuse == "true", output);
        Assert.Equal("100", run);
    }

    [Fact]
    public void The_newest_matching_run_wins()
    {
        Run(100);
        Run(101, RunOf(101, createdAt: "2026-11-06T11:00:00Z"));
        var (_, run, _, identity) = Decide();
        identity.Dispose();
        Assert.Equal("101", run);
    }

    [Fact]
    public void An_older_run_of_the_same_tree_is_reused_past_a_newer_run_of_another_tree()
    {
        Run(100);
        Run(101, RunOf(101, createdAt: "2026-11-06T11:00:00Z"), tree: "cccc");
        var (_, run, _, identity) = Decide();
        identity.Dispose();
        Assert.Equal("100", run);
    }

    [Fact]
    public void A_different_code_tree_runs()
    {
        Run(100, tree: "cccc");
        AssertNoReuse("another code tree");
    }

    /// <summary>docs/ is outside the code tree: a commit that changes its data must not reuse a run that never saw it.</summary>
    [Fact]
    public void Other_inputs_outside_the_code_tree_run()
    {
        Run(100, inputs: "dddd");
        AssertNoReuse("other inputs outside the code tree");
    }

    /// <summary>GitHub lists any open pull request of the branch; a closed one on the same branch name is another pull request.</summary>
    [Fact]
    public void A_run_that_ran_for_another_pull_request_is_not_reused()
    {
        Run(100, identityPullRequest: "65");
        AssertNoReuse("ran for pull request #65");
    }

    [Theory]
    [InlineData("docs/sov-zt/zero-trust-matrix.json", true)]
    [InlineData("docs/str-legal/control-catalog.json", true)]
    [InlineData("docs/qa/dated-obligations.json", true)]
    [InlineData("docs/qa/notes.md", true)]
    [InlineData("docs/CI_AND_TESTING.md", false)]
    [InlineData("docs/acceptance-plans/PLAN-66-qa-ci-fast-pipeline.md", false)]
    public void What_outside_the_code_tree_counts_for_reuse(string path, bool counts)
    {
        var wrapper = Path.Combine(_dir, "outside.ps1");
        System.IO.File.WriteAllText(wrapper, $$"""
            param([string]$RepoRoot)
            . (Join-Path $RepoRoot "scripts/acceptance-evidence-lib.ps1")
            . (Join-Path $RepoRoot "scripts/ci/acceptance-stage-lib.ps1")
            $scope = Read-AcceptanceJson -Path (Join-Path $RepoRoot "tests/acceptance/ci-scope.json")
            Write-Output "counts=$(Test-AcceptanceOutsideInput -ScopeFile $scope -Path '{{path}}')"
            """);
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(wrapper);
        Assert.True(exit == 0, stdout + stderr);
        Assert.Contains($"counts={counts}", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_fork_runs()
    {
        Run(100);
        AssertNoReuse("is in someone/HRManagement", headRepository: "someone/HRManagement");
    }

    [Fact]
    public void A_candidate_from_a_fork_is_not_reused()
    {
        Run(100, RunOf(100, headRepository: "someone/HRManagement"));
        AssertNoReuse("comes from someone/HRManagement");
    }

    [Theory]
    [InlineData("push")]
    [InlineData("schedule")]
    [InlineData("workflow_dispatch")]
    public void Only_a_pull_request_run_reuses(string @event)
    {
        Run(100);
        AssertNoReuse("only a pull request run reuses", @event: @event);
    }

    [Theory]
    [InlineData("AcQa35RemoteCiTests", "")]
    [InlineData("", "AcQa35RemoteCiTests.Ac35_a")]
    public void A_narrowed_run_never_reuses_and_says_so_in_its_identity(string classes, string simulate)
    {
        Run(100);
        var (reuse, _, _, identity) = Decide(classes: classes, simulate: simulate);
        using (identity)
        {
            Assert.Equal("false", reuse);
            Assert.Equal("narrowed", identity.RootElement.GetProperty("scope").GetString());
        }
    }

    [Fact]
    public void A_narrowed_run_is_not_reused()
    {
        Run(100, scope: "narrowed");
        AssertNoReuse("has scope narrowed");
    }

    [Theory]
    [InlineData("failure", "completed")]
    [InlineData("cancelled", "completed")]
    [InlineData("", "in_progress")]
    public void A_run_that_is_not_green_is_not_reused(string conclusion, string status)
    {
        Run(100, RunOf(100, conclusion: conclusion, status: status));
        AssertNoReuse("run 100 is");
    }

    [Theory]
    [InlineData("skipped")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    public void A_run_with_a_job_that_did_not_succeed_is_not_reused(string conclusion)
    {
        Run(100, jobConclusions: ["success", conclusion]);
        AssertNoReuse("a job that did not succeed");
    }

    [Fact]
    public void A_run_of_another_pull_request_is_not_reused()
    {
        Run(100, RunOf(100, pullRequest: 67));
        AssertNoReuse("does not belong to pull request #66");
    }

    [Fact]
    public void A_run_that_names_no_pull_request_is_not_reused()
    {
        Run(100, RunOf(100, pullRequest: 0));
        AssertNoReuse("does not belong to pull request #66");
    }

    [Fact]
    public void A_run_of_another_event_is_not_reused()
    {
        Run(100, RunOf(100, @event: "workflow_dispatch"));
        AssertNoReuse("workflow_dispatch run");
    }

    [Fact]
    public void A_run_without_an_identity_is_not_reused()
    {
        Run(100, identity: false);
        AssertNoReuse("has no run identity");
    }

    [Fact]
    public void An_identity_of_another_repository_is_not_reused()
    {
        Run(100, repository: "someone/HRManagement");
        AssertNoReuse("names someone/HRManagement");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void A_run_whose_evidence_is_gone_is_not_reused(bool evidence, bool expired)
    {
        Run(100, evidence: evidence, expired: expired);
        AssertNoReuse("no evidence artifact left");
    }

    [Fact]
    public void A_chain_to_an_original_run_that_no_longer_holds_is_not_reused()
    {
        Run(100, RunOf(100, conclusion: "failure"), listed: false);
        Run(101, RunOf(101), reusedFrom: "100");
        AssertNoReuse("run 100 is");
    }

    [Fact]
    public void A_chain_to_a_run_that_is_not_original_is_not_reused()
    {
        Run(99, listed: false);
        Run(100, listed: false, reusedFrom: "99");
        Run(101, RunOf(101), reusedFrom: "100");
        AssertNoReuse("not an original full run");
    }

    [Fact]
    public void No_earlier_run_runs()
    {
        AssertNoReuse("no successful run of pull request #66 yet");
    }

    [Fact]
    public void An_error_of_the_check_runs_everything()
    {
        Run(100);
        System.IO.File.Delete(Path.Combine(_dir, "jobs-100.json"));
        var (reuse, _, _, identity) = Decide();
        identity.Dispose();
        Assert.Equal("false", reuse);
    }

    // ---------------------------------------------------------------- the evidence of a reusing run

    private (int Exit, string Output) CopyEvidence(string summaryTree, string summaryRun)
    {
        var source = Path.Combine(_dir, "artifact");
        Directory.CreateDirectory(Path.Combine(source, "acceptance-evidence"));
        System.IO.File.WriteAllText(Path.Combine(source, "acceptance-evidence", "summary.json"),
            JsonSerializer.Serialize(new { codeTree = summaryTree, ciRunId = summaryRun }));
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(
            Path.Combine(RepoPaths.Root, "scripts", "ci", "acceptance-reuse-evidence.ps1"),
            $"-Run 100 -Repository {Repo} -Target \"{Path.Combine(_dir, "run")}\" -FixtureDir \"{source}\" -CodeTree {Tree}");
        return (exit, stdout + stderr);
    }

    [Fact]
    public void The_evidence_of_the_reused_run_is_copied_with_a_pointer()
    {
        var (exit, output) = CopyEvidence(Tree, "100");
        Assert.True(exit == 0, output);
        Assert.True(System.IO.File.Exists(Path.Combine(_dir, "run", "acceptance-evidence", "summary.json")));
        using var pointer = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(_dir, "run", "reuse.json")));
        Assert.Equal("100", pointer.RootElement.GetProperty("reusedFrom").GetString());
        Assert.EndsWith("/actions/runs/100", pointer.RootElement.GetProperty("url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Evidence_of_another_code_tree_is_refused()
    {
        var (exit, output) = CopyEvidence("cccc", "100");
        Assert.NotEqual(0, exit);
        Assert.Contains("is for code tree cccc", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Evidence_that_comes_from_another_run_is_refused()
    {
        var (exit, output) = CopyEvidence(Tree, "200");
        Assert.NotEqual(0, exit);
        Assert.Contains("comes from run 200", output, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the workflow

    private static string Workflow =>
        System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml")).Replace("\r\n", "\n");

    private static string Job(string key)
    {
        var text = Workflow;
        var start = text.IndexOf($"\n  {key}:\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"ci.yml has no job {key}");
        var next = Regex.Match(text[(start + 3)..], @"\n  [A-Za-z0-9_-]+:\n");
        return next.Success ? text.Substring(start, next.Index + 3) : text[start..];
    }

    [Theory]
    [InlineData("build-and-test")]
    [InlineData("e2e")]
    [InlineData("acceptance-build")]
    [InlineData("acceptance-stage")]
    [InlineData("acceptance")]
    public void The_heavy_jobs_wait_for_the_reuse_check_and_learn_the_reused_run(string key)
    {
        var job = Job(key);
        Assert.Matches(@"needs: \[[^\]]*\breuse-check\b[^\]]*\]", job);
        Assert.Contains("REUSED_RUN: ${{ needs.reuse-check.outputs.reused-run }}", job, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("codeql")]
    [InlineData("gitleaks")]
    public void CodeQL_and_Gitleaks_always_run(string key)
    {
        var job = Job(key);
        Assert.DoesNotContain("reuse-check", job, StringComparison.Ordinal);
        Assert.DoesNotContain("REUSED_RUN", job, StringComparison.Ordinal);
    }

    /// <summary>An evidence commit is the case a reusing run exists for; its evidence is still checked (P-4 of PLAN-41).</summary>
    [Fact]
    public void The_committed_evidence_check_runs_in_a_reusing_run()
    {
        Assert.Contains("- name: Committed acceptance evidence (PLAN-41)\n        env:\n          GH_TOKEN: ${{ github.token }}\n          # PLAN-66 (S2): an evidence commit is what a reusing run is for, so this one step always runs.\n          REUSED_RUN: \"\"\n", Job("build-and-test"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_run_steps_script_does_no_step_of_a_reusing_run_and_names_the_run()
    {
        var script = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "ci", "run-steps.sh"));
        Assert.Contains("if [[ -n \"${REUSED_RUN:-}\" && \"$MODE\" == \"one\" ]]; then", script, StringComparison.Ordinal);
        Assert.Contains("scripts/ci/reused-note.sh", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reuse_check_leaves_the_identity_of_every_run()
    {
        var job = Job("reuse-check");
        Assert.Contains("name: run-identity\n", job, StringComparison.Ordinal);
        Assert.Contains("scripts/ci/acceptance-reuse.ps1", job, StringComparison.Ordinal);
        Assert.Contains("actions: read", job, StringComparison.Ordinal);
        Assert.DoesNotContain(": write", job, StringComparison.Ordinal);
    }

    [Fact]
    public void Acceptance_copies_the_evidence_of_the_reused_run()
    {
        var job = Job("acceptance");
        Assert.Contains("if: ${{ !cancelled() && env.REUSED_RUN != '' }}\n        run: pwsh -NoProfile -File scripts/ci/acceptance-reuse-evidence.ps1 -Run \"$REUSED_RUN\"", job, StringComparison.Ordinal);
        Assert.Contains("- name: Assemble the evidence\n        if: ${{ !cancelled() && env.REUSED_RUN == '' }}", job, StringComparison.Ordinal);
    }
}
