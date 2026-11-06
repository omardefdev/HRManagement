using System.Text.Json;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-66 (AC-QA-66f): acceptance-job.ps1 runs chosen stages, the stage results merge into the layout a sequential run writes, and
/// a stage job refuses an image that does not carry the commit under test.
/// </summary>
public sealed class Qa066StageTests : IDisposable
{
    private const string Sha = "1111111111111111111111111111111111111111";
    private const string Tree = "aaaa";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qa066-stage-" + Guid.NewGuid().ToString("N"));

    public Qa066StageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static string Script(params string[] parts) =>
        Path.Combine([RepoPaths.Root, "scripts", "ci", .. parts]);

    private static string[] OrderOfTheScope()
    {
        using var scope = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "tests", "acceptance", "ci-scope.json")));
        return scope.RootElement.GetProperty("order").EnumerateArray()
            .Select(e => $"{e.GetProperty("part").GetString()}:{e.GetProperty("group").GetString()}")
            .ToArray();
    }

    private static (int Exit, string[] Lines, string Output) List(string extra = "")
    {
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(Script("acceptance-job.ps1"), $"-ListStages {extra}");
        return (exit, stdout.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), stdout + stderr);
    }

    [Fact]
    public void Without_stage_every_entry_runs_in_the_order_of_the_scope()
    {
        var (exit, lines, output) = List();
        Assert.True(exit == 0, output);
        Assert.Equal(OrderOfTheScope(), lines);
    }

    [Fact]
    public void Stage_takes_only_the_named_entries_in_the_order_of_the_scope()
    {
        var (exit, lines, output) = List("-Stage lab:isolated,lab:main");
        Assert.True(exit == 0, output);
        Assert.Equal(["lab:main", "lab:isolated"], lines);
    }

    [Theory]
    [InlineData("lab:nothing")]
    [InlineData("lab:main,prodLike:nothing")]
    [InlineData("main")]
    public void An_unknown_stage_fails(string stage)
    {
        var (exit, _, output) = List($"-Stage {stage}");
        Assert.NotEqual(0, exit);
        Assert.Contains("Unknown stage", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stage_named_twice_fails()
    {
        var (exit, _, output) = List("-Stage lab:main,lab:main");
        Assert.NotEqual(0, exit);
        Assert.Contains("twice", output, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the merge

    private const string ScopeJson = """
        {
          "order": [
            { "part": "lab", "group": "main", "stack": "lab" },
            { "part": "lab", "group": "isolated", "stack": "lab" },
            { "part": "prodLike", "group": "main", "stack": "prodlike" },
            { "part": "lab", "group": "meta", "stack": "base", "ciOnly": true }
          ],
          "stageJobs": [
            { "name": "one", "stages": ["lab:main"] },
            { "name": "two", "stages": ["lab:isolated", "prodLike:main"] },
            { "name": "meta", "stages": ["lab:meta"] }
          ],
          "metaRule": { "stage": "lab:meta" },
          "lab": [
            { "class": "MainTests", "required": true, "group": "main" },
            { "class": "IsolatedTests", "required": true, "group": "isolated" },
            { "class": "MetaTests", "required": true, "group": "meta" }
          ],
          "prodLike": [ { "class": "ProdTests", "required": true, "group": "main" } ],
          "excluded": []
        }
        """;

    private string Stage(string name, string[] stages, string? sourceSha = null, string[]? failures = null)
    {
        var folder = Path.Combine(_dir, "stages", name);
        Directory.CreateDirectory(Path.Combine(folder, "acceptance-evidence"));
        Directory.CreateDirectory(Path.Combine(folder, "TestResults"));
        Directory.CreateDirectory(Path.Combine(folder, "logs"));
        System.IO.File.WriteAllText(Path.Combine(folder, "stage.json"), JsonSerializer.Serialize(new
        {
            name, stages, sourceSha = sourceSha ?? Sha, codeTree = Tree, failures = failures ?? []
        }));
        System.IO.File.WriteAllText(Path.Combine(folder, "job-timing.json"), """{"steps":[{"step":"Prepare","seconds":1}]}""");
        return folder;
    }

    private static void Evidence(string stage, string ac, string file, string content)
    {
        var path = Path.Combine(stage, "acceptance-evidence", ac, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
    }

    private static string Result(params (string Key, string Status)[] scenarios) => JsonSerializer.Serialize(new
    {
        scenarios = scenarios.ToDictionary(s => s.Key, s => new { status = s.Status, reason = s.Status == "Pass" ? null : "why", testClass = "T" }),
        status = "ignored",
        correlationId = "c",
        recordedAtUtc = "2026-11-06T00:00:00.0000000Z"
    });

    private (int Exit, string Output, JsonDocument Report) Merge(string extra = "")
    {
        var scope = Path.Combine(_dir, "scope.json");
        System.IO.File.WriteAllText(scope, ScopeJson);
        var report = Path.Combine(_dir, "report.json");
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(Script("acceptance-merge.ps1"),
            $"-Scope \"{scope}\" -StagesDir \"{Path.Combine(_dir, "stages")}\" -RunDir \"{Path.Combine(_dir, "run")}\" -SourceSha {Sha} -CodeTree {Tree} -ReportFile \"{report}\" {extra}");
        return (exit, stdout + stderr, JsonDocument.Parse(System.IO.File.ReadAllText(report)));
    }

    private static string[] Failures(JsonDocument report) =>
        report.RootElement.GetProperty("failures").EnumerateArray().Select(f => f.GetString()!).ToArray();

    private string Run(params string[] parts) => Path.Combine([_dir, "run", .. parts]);

    private void AllStages()
    {
        var one = Stage("one", ["lab:main"]);
        var two = Stage("two", ["lab:isolated", "prodLike:main"]);
        var meta = Stage("meta", ["lab:meta"]);
        System.IO.File.WriteAllText(Path.Combine(one, "TestResults", "Tests lab-main.trx"), "<TestRun/>");
        System.IO.File.WriteAllText(Path.Combine(two, "TestResults", "Tests prodLike-main.trx"), "<TestRun/>");
        System.IO.File.WriteAllText(Path.Combine(one, "logs", "Tests lab-main.log"), "one");
        System.IO.File.WriteAllText(Path.Combine(one, "logs", "disk-lab.txt"), "one");
        System.IO.File.WriteAllText(Path.Combine(two, "logs", "disk-lab.txt"), "two");
        Evidence(one, "AC-01", "result.json", Result(("a", "Pass")));
        Evidence(one, "AC-01", "metadata.json", """{"from":"one"}""");
        Evidence(one, "AC-01", "http/first.json", "{}");
        Evidence(two, "AC-02", "result.json", Result(("b", "Pass")));
        Evidence(meta, "AC-QA-34", "result.json", Result(("34a", "Pass")));
        System.IO.File.WriteAllText(Path.Combine(one, "acceptance-evidence", "summary.md"), "- **AC-01**: Pass (`one`)\n");
        System.IO.File.WriteAllText(Path.Combine(two, "acceptance-evidence", "summary.md"), "- **AC-02**: Pass (`two`)\n- **AC-01**: Pass (`one`)\n");
        System.IO.File.WriteAllText(Path.Combine(two, "acceptance-evidence", "summary.json"), "{}");
    }

    [Fact]
    public void The_stage_results_merge_into_the_layout_of_a_sequential_run()
    {
        AllStages();
        var (exit, output, report) = Merge();
        using (report)
        {
            Assert.True(exit == 0, output);
            Assert.Empty(Failures(report));
            Assert.True(System.IO.File.Exists(Run("TestResults", "Tests lab-main.trx")));
            Assert.True(System.IO.File.Exists(Run("TestResults", "Tests prodLike-main.trx")));
            Assert.True(System.IO.File.Exists(Run("logs", "Tests lab-main.log")));
            Assert.True(System.IO.File.Exists(Run("acceptance-evidence", "AC-01", "result.json")));
            Assert.True(System.IO.File.Exists(Run("acceptance-evidence", "AC-01", "http", "first.json")));
            Assert.True(System.IO.File.Exists(Run("acceptance-evidence", "AC-02", "result.json")));
            Assert.True(System.IO.File.Exists(Run("acceptance-evidence", "AC-QA-34", "result.json")));
            Assert.False(System.IO.File.Exists(Run("acceptance-evidence", "summary.json")));
            Assert.Equal(["- **AC-01**: Pass (`one`)", "- **AC-02**: Pass (`two`)"], System.IO.File.ReadAllLines(Run("acceptance-evidence", "summary.md")));
            // The runner of each job leaves its own stack report; neither hides the other.
            Assert.Equal("one", System.IO.File.ReadAllText(Run("logs", "disk-lab.txt")));
            Assert.Equal("two", System.IO.File.ReadAllText(Run("logs", "two-disk-lab.txt")));
            Assert.Equal(3, report.RootElement.GetProperty("stages").GetArrayLength());
        }
    }

    [Fact]
    public void Two_stages_in_one_folder_keep_both_scenarios_and_the_worst_status()
    {
        AllStages();
        Evidence(Path.Combine(_dir, "stages", "two"), "AC-01", "result.json", Result(("b", "Fail")));
        Evidence(Path.Combine(_dir, "stages", "two"), "AC-01", "metadata.json", """{"from":"two"}""");
        var (exit, output, report) = Merge();
        report.Dispose();
        Assert.True(exit == 0, output);

        using var merged = JsonDocument.Parse(System.IO.File.ReadAllText(Run("acceptance-evidence", "AC-01", "result.json")));
        var scenarios = merged.RootElement.GetProperty("scenarios");
        Assert.Equal("Pass", scenarios.GetProperty("a").GetProperty("status").GetString());
        Assert.Equal("Fail", scenarios.GetProperty("b").GetProperty("status").GetString());
        Assert.Equal("Fail", merged.RootElement.GetProperty("status").GetString());
        Assert.Contains("why", merged.RootElement.GetProperty("reason").GetString(), StringComparison.Ordinal);
        // As in a sequential run, the later stage is the last writer of everything else.
        Assert.Contains("two", System.IO.File.ReadAllText(Run("acceptance-evidence", "AC-01", "metadata.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_stage_fails_the_merge()
    {
        Stage("one", ["lab:main"]);
        Stage("meta", ["lab:meta"]);
        var (_, _, report) = Merge();
        using (report)
            Assert.Contains("Stage two left no result.", Failures(report));
    }

    [Fact]
    public void A_stage_of_another_commit_fails_the_merge()
    {
        AllStages();
        Stage("one", ["lab:main"], sourceSha: "2222222222222222222222222222222222222222");
        var (_, _, report) = Merge();
        using (report)
            Assert.Contains(Failures(report), f => f.StartsWith("Stage one ran on 2222", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stage_that_ran_other_entries_fails_the_merge()
    {
        AllStages();
        Stage("one", ["lab:isolated"]);
        var (_, _, report) = Merge();
        using (report)
            Assert.Contains(Failures(report), f => f.Contains("the scope gives it lab:main", StringComparison.Ordinal));
    }

    [Fact]
    public void The_failures_of_a_stage_reach_the_merge()
    {
        AllStages();
        Stage("two", ["lab:isolated", "prodLike:main"], failures: ["The guard failed: see summary.md."]);
        var (_, _, report) = Merge();
        using (report)
            Assert.Contains("Stage two: The guard failed: see summary.md.", Failures(report));
    }

    [Fact]
    public void A_narrowed_run_expects_only_the_stages_that_hold_its_classes()
    {
        Stage("one", ["lab:main"]);
        var (_, _, report) = Merge("-Limit MainTests -Ran lab");
        using (report)
            Assert.Empty(Failures(report));
    }

    // ---------------------------------------------------------------- the images

    private (int Exit, string Output) Verify(string inspect)
    {
        System.IO.File.WriteAllText(Path.Combine(_dir, "images.json"), $$"""
            { "sourceSha": "{{Sha}}", "services": ["hrm-acceptance-hr-gateway:latest", "hr-migrate:local"], "local": ["hr-rabbitmq:local"],
              "thirdParty": ["postgres:16-alpine"], "tools": [] }
            """);
        var inspectFile = Path.Combine(_dir, "inspect.json");
        System.IO.File.WriteAllText(inspectFile, inspect);
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(Script("acceptance-images.ps1"),
            $"-Action Verify -SourceSha {Sha} -Directory \"{_dir}\" -InspectFile \"{inspectFile}\"");
        return (exit, stdout + stderr);
    }

    [Fact]
    public void The_image_check_passes_images_of_the_commit_under_test()
    {
        var (exit, output) = Verify($$"""
            { "hrm-acceptance-hr-gateway:latest": "{{Sha}}", "hr-migrate:local": "{{Sha}}", "hr-rabbitmq:local": "", "postgres:16-alpine": "" }
            """);
        Assert.True(exit == 0, output);
    }

    [Fact]
    public void The_image_check_refuses_a_stale_image()
    {
        var (exit, output) = Verify($$"""
            { "hrm-acceptance-hr-gateway:latest": "2222", "hr-migrate:local": "{{Sha}}", "hr-rabbitmq:local": "", "postgres:16-alpine": "" }
            """);
        Assert.Equal(1, exit);
        Assert.Contains("hrm-acceptance-hr-gateway:latest carries revision '2222'", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_image_check_refuses_an_image_built_from_a_changed_tree()
    {
        var (exit, output) = Verify($$"""
            { "hrm-acceptance-hr-gateway:latest": "{{Sha}}-dirty", "hr-migrate:local": "{{Sha}}", "hr-rabbitmq:local": "", "postgres:16-alpine": "" }
            """);
        Assert.Equal(1, exit);
        Assert.Contains("-dirty", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_image_check_refuses_a_missing_image()
    {
        var (exit, output) = Verify($$"""
            { "hrm-acceptance-hr-gateway:latest": "{{Sha}}", "hr-rabbitmq:local": "" }
            """);
        Assert.Equal(1, exit);
        Assert.Contains("hr-migrate:local is missing", output, StringComparison.Ordinal);
        Assert.Contains("postgres:16-alpine is missing", output, StringComparison.Ordinal);
    }
}
