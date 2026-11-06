using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-66 (AC-QA-66a, AC-QA-66b): the parallel Acceptance job. Every entry of order[] belongs to exactly one stage job, the matrix of
/// ci.yml is stageJobs[] of the scope, and the Acceptance check cannot be green with a stage job that did not succeed.
/// </summary>
public class Qa066WorkflowTests
{
    private static string Workflow =>
        System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml")).Replace("\r\n", "\n");

    private static JsonDocument Scope() =>
        JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "tests", "acceptance", "ci-scope.json")));

    /// <summary>The text of one job of ci.yml: from its key to the next job key.</summary>
    private static string Job(string key)
    {
        var text = Workflow;
        var start = text.IndexOf($"\n  {key}:\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"ci.yml has no job {key}");
        var next = Regex.Match(text[(start + 3)..], @"\n  [A-Za-z0-9_-]+:\n");
        return next.Success ? text.Substring(start, next.Index + 3) : text[start..];
    }

    private static string Key(JsonElement stage) => $"{stage.GetProperty("part").GetString()}:{stage.GetProperty("group").GetString()}";

    private static List<(string Name, string[] Stages)> StageJobs(JsonElement scope) =>
        scope.GetProperty("stageJobs").EnumerateArray()
            .Select(j => (j.GetProperty("name").GetString()!, j.GetProperty("stages").EnumerateArray().Select(s => s.GetString()!).ToArray()))
            .ToList();

    private static List<(string Name, string[] Stages)> Matrix()
    {
        var job = Job("acceptance-stage");
        return Regex.Matches(job, @"- name: ([a-z-]+)\n\s+stages: ([^\n]+)\n\s+timeout: \d+")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value.Split(',')))
            .ToList();
    }

    [Fact]
    public void Every_entry_of_order_belongs_to_exactly_one_stage_job()
    {
        using var document = Scope();
        var scope = document.RootElement;
        var order = scope.GetProperty("order").EnumerateArray().Select(Key).ToList();
        var inJobs = StageJobs(scope).SelectMany(j => j.Stages).ToList();
        Assert.Equal(order.Order(StringComparer.Ordinal), inJobs.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_class_of_the_scope_runs_in_exactly_one_stage_job()
    {
        using var document = Scope();
        var scope = document.RootElement;
        var stageOf = StageJobs(scope).SelectMany(j => j.Stages.Select(s => (Stage: s, Job: j.Name))).ToDictionary(x => x.Stage, x => x.Job);
        var classes = new[] { "lab", "prodLike" }
            .SelectMany(part => scope.GetProperty(part).EnumerateArray().Select(e => (Class: e.GetProperty("class").GetString()!, Stage: $"{part}:{e.GetProperty("group").GetString()}")))
            .ToList();
        Assert.Equal(classes.Count, classes.Select(c => c.Class).Distinct(StringComparer.Ordinal).Count());
        Assert.All(classes, c => Assert.True(stageOf.ContainsKey(c.Stage), $"{c.Class}: no stage job holds {c.Stage}"));
    }

    [Fact]
    public void The_matrix_of_the_workflow_is_the_stage_jobs_of_the_scope()
    {
        using var document = Scope();
        var expected = StageJobs(document.RootElement).OrderBy(j => j.Name, StringComparer.Ordinal).ToList();
        var matrix = Matrix().OrderBy(j => j.Name, StringComparer.Ordinal).ToList();
        Assert.Equal(expected.Select(j => j.Name), matrix.Select(j => j.Name));
        for (var i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i].Stages.Order(StringComparer.Ordinal), matrix[i].Stages.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_stage_of_a_job_waits_only_for_jobs_of_the_scope()
    {
        using var document = Scope();
        var scope = document.RootElement;
        var names = StageJobs(scope).Select(j => j.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var job in scope.GetProperty("stageJobs").EnumerateArray())
        {
            if (!job.TryGetProperty("waitFor", out var waitFor))
                continue;
            foreach (var name in waitFor.EnumerateArray().Select(n => n.GetString()!))
            {
                Assert.Contains(name, names);
                Assert.NotEqual(job.GetProperty("name").GetString(), name);
            }
        }
    }

    /// <summary>AC-QA-41: 41a, 41e and 41g read the folders of the lab and prod-like stages that come before final in order[].</summary>
    [Fact]
    public void The_final_stage_reads_every_stage_that_comes_before_it()
    {
        using var document = Scope();
        var scope = document.RootElement;
        var order = scope.GetProperty("order").EnumerateArray().Select(Key).ToList();
        var before = order.Take(order.IndexOf("prodLike:final")).ToList();
        var final = scope.GetProperty("stageJobs").EnumerateArray().Single(j => j.GetProperty("name").GetString() == "prodlike-final");
        var waited = final.GetProperty("waitFor").EnumerateArray().Select(n => n.GetString()!).ToHashSet(StringComparer.Ordinal);
        var read = StageJobs(scope).Where(j => waited.Contains(j.Name)).SelectMany(j => j.Stages).ToHashSet(StringComparer.Ordinal);
        Assert.All(before, stage => Assert.Contains(stage, read));
    }

    [Fact]
    public void A_stage_that_applies_an_overlay_stays_the_last_of_its_stack_in_its_job()
    {
        using var document = Scope();
        var scope = document.RootElement;
        var order = scope.GetProperty("order").EnumerateArray().ToList();
        foreach (var overlaid in order.Where(s => s.TryGetProperty("composeFiles", out _)))
        {
            var job = StageJobs(scope).Single(j => j.Stages.Contains(Key(overlaid)));
            var position = order.FindIndex(s => Key(s) == Key(overlaid));
            Assert.DoesNotContain(job.Stages, s =>
                order.FindIndex(o => Key(o) == s) > position
                && order.Single(o => Key(o) == s).GetProperty("stack").GetString() == overlaid.GetProperty("stack").GetString());
        }
    }

    [Theory]
    [InlineData("CI")]
    [InlineData("E2E")]
    [InlineData("CodeQL")]
    [InlineData("Gitleaks")]
    [InlineData("Acceptance")]
    public void The_required_check_names_stay_job_names(string check) =>
        Assert.Contains($"    name: {check}\n", Workflow, StringComparison.Ordinal);

    [Fact]
    public void Acceptance_needs_every_stage_job_and_always_runs()
    {
        var job = Job("acceptance");
        Assert.Contains("    name: Acceptance\n", job, StringComparison.Ordinal);
        Assert.Matches(@"needs: \[[^\]]*\bacceptance-stage\b[^\]]*\]", job);
        Assert.Matches(@"needs: \[[^\]]*\bacceptance-build\b[^\]]*\]", job);
        // A skipped required check counts as passed on GitHub, so nothing a stage job does may skip this one.
        Assert.Contains("    if: ${{ always() && needs.dedupe.outputs.skip != 'true' }}\n", job, StringComparison.Ordinal);
        Assert.Matches(@"- name: Every stage job succeeded\n\s+if: always\(\)\n\s+env:\n\s+NEEDS_JSON: \$\{\{ toJSON\(needs\) \}\}\n\s+run: pwsh -NoProfile -File scripts/ci/acceptance-needs-check\.ps1", job);
        Assert.DoesNotContain("continue-on-error", job, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", Job("acceptance-stage"), StringComparison.Ordinal);
    }

    [Fact]
    public void One_failed_stage_does_not_stop_the_others()
    {
        var job = Job("acceptance-stage");
        Assert.Contains("fail-fast: false", job, StringComparison.Ordinal);
    }

    [Fact]
    public void Acceptance_keeps_the_evidence_contract()
    {
        var job = Job("acceptance");
        Assert.Contains("name: acceptance-evidence\n", job, StringComparison.Ordinal);
        Assert.Contains("path: artifacts/acceptance/run/\n", job, StringComparison.Ordinal);
        Assert.Contains("retention-days: 30\n", job, StringComparison.Ordinal);
        Assert.Contains("scripts/ci/acceptance-artifact-scan.ps1", job, StringComparison.Ordinal);
        Assert.Contains("steps.artifact-scan.outcome == 'success'", job, StringComparison.Ordinal);
        Assert.Contains("scripts/ci/acceptance-job.ps1 -Profile Both -MergeStages", job, StringComparison.Ordinal);
    }

    /// <summary>Each runner generates its own passwords: only the stage itself can look for them in what it uploads.</summary>
    [Fact]
    public void Each_stage_scans_its_results_before_it_uploads_them()
    {
        var job = Job("acceptance-stage");
        Assert.Contains("scripts/ci/acceptance-artifact-scan.ps1 -Target artifacts/acceptance/stages/${{ matrix.name }}", job, StringComparison.Ordinal);
        Assert.Contains("if: ${{ !cancelled() && env.RUN_STAGE == 'true' && steps.artifact-scan.outcome == 'success' }}", job, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage_jobs_run_on_ubuntu_24_and_take_their_images_from_the_build_job()
    {
        var job = Job("acceptance-stage");
        Assert.Contains("    runs-on: ubuntu-24.04\n", job, StringComparison.Ordinal);
        Assert.Contains("name: acceptance-images\n", job, StringComparison.Ordinal);
        Assert.Contains("-Action Load", job, StringComparison.Ordinal);
        Assert.Contains("-Action Verify -SourceSha ${{ github.sha }}", job, StringComparison.Ordinal);
        Assert.Contains("-NoBuild", job, StringComparison.Ordinal);
        Assert.DoesNotContain("docker pull", job, StringComparison.Ordinal);
        Assert.DoesNotContain("docker login", job, StringComparison.Ordinal);
    }

    [Fact]
    public void The_image_archive_lives_for_a_day()
    {
        var job = Job("acceptance-build");
        Assert.Contains("name: acceptance-images\n", job, StringComparison.Ordinal);
        Assert.Contains("retention-days: 1\n", job, StringComparison.Ordinal);
        Assert.Contains("-Action Build -SourceSha ${{ github.sha }}", job, StringComparison.Ordinal);
    }

    [Fact]
    public void The_manual_inputs_reach_the_stage_jobs_and_the_merge()
    {
        foreach (var job in new[] { Job("acceptance-stage"), Job("acceptance") })
        {
            Assert.Contains("ACCEPTANCE_SIMULATE_MISSING: ${{ inputs.simulate_missing }}", job, StringComparison.Ordinal);
            Assert.Contains("ACCEPTANCE_CLASSES: ${{ inputs.classes }}", job, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_new_push_to_a_pull_request_cancels_the_run_it_replaces()
    {
        Assert.Contains("concurrency:\n  group: ci-${{ github.event_name == 'pull_request' && github.ref || github.run_id }}\n  cancel-in-progress: ${{ github.event_name == 'pull_request' }}\n", Workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void E2E_does_not_wait_for_CI()
    {
        var job = Job("e2e");
        Assert.DoesNotMatch(@"needs: \[[^\]]*build-and-test", job);
        Assert.DoesNotContain("needs.build-and-test", job, StringComparison.Ordinal);
    }

    /// <summary>The overlays bring no build of their own: the images the build job saves are all a stage job needs.</summary>
    [Theory]
    [InlineData("docker-compose.acceptance.yml")]
    [InlineData("docker-compose.acceptance-prodlike.yml")]
    [InlineData("docker-compose.acceptance-leastpriv.yml")]
    [InlineData("docker-compose.acceptance-s2s42.yml")]
    public void The_acceptance_overlays_build_nothing(string file)
    {
        var text = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, file));
        Assert.DoesNotMatch(@"(?m)^\s+build:", text);
    }

    [Fact]
    public void The_least_privilege_overlay_uses_no_third_party_image_of_its_own()
    {
        var text = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "docker-compose.acceptance-leastpriv.yml"));
        var images = Regex.Matches(text, @"(?m)^\s+image:\s*(\S+)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal(["hr-migrate:local"], images);
    }

    // ---------------------------------------------------------------- the needs check

    private static (int Exit, string Output) NeedsCheck(string json)
    {
        var folder = Path.Combine(Path.GetTempPath(), "qa066-needs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // Through a file: the quotes of the JSON do not survive a command line.
            var wrapper = Path.Combine(folder, "run.ps1");
            System.IO.File.WriteAllText(wrapper, $$"""
                param([string]$RepoRoot)
                $env:NEEDS_JSON = '{{json}}'
                & "{{Path.Combine(RepoPaths.Root, "scripts", "ci", "acceptance-needs-check.ps1")}}"
                exit $LASTEXITCODE
                """);
            var (exit, stdout, stderr) = RepoScriptRunner.RunScript(wrapper);
            return (exit, stdout + stderr);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_needs_check_passes_when_every_job_succeeded()
    {
        var (exit, output) = NeedsCheck("""{"dedupe":{"result":"success"},"acceptance-stage":{"result":"success"}}""");
        Assert.True(exit == 0, output);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancelled")]
    [InlineData("skipped")]
    public void The_needs_check_refuses_a_job_that_did_not_succeed(string result)
    {
        var (exit, output) = NeedsCheck("""{"dedupe":{"result":"success"},"acceptance-stage":{"result":"RESULT"}}""".Replace("RESULT", result, StringComparison.Ordinal));
        Assert.Equal(1, exit);
        Assert.Contains($"acceptance-stage: {result}", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_needs_check_refuses_an_empty_context()
    {
        var (exit, _) = NeedsCheck("{}");
        Assert.Equal(1, exit);
    }
}
