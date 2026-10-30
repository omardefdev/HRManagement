using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-35 (G-QA-07, G-QA-11, G-QA-15): the GitHub workflows stay honest. A <c>run</c> script never carries a literal
/// <c>\n</c> outside a quoted string (e2e-journeys failed on every run because of one), every <c>dotnet restore</c> uses
/// <c>--locked-mode</c>, scheduled jobs wait for <c>ENABLE_SCHEDULED</c> (except the dated-obligations sweep), the
/// duplicate workflows stay deleted and the per-PR CodeQL analysis lives only in ci.yml with its SARIF gate.
/// actionlint (scripts/ci/actionlint.sh, a step of scripts/ci/steps.json) checks the rest of the syntax.
/// </summary>
public class WorkflowYamlLintTests
{
    private static string WorkflowsDir => Path.Combine(RepoPaths.Root, ".github", "workflows");

    private static IEnumerable<string> WorkflowFiles() =>
        Directory.EnumerateFiles(WorkflowsDir, "*.yml").Concat(Directory.EnumerateFiles(WorkflowsDir, "*.yaml")).OrderBy(f => f, StringComparer.Ordinal);

    private sealed record RunScript(int Line, string Text);

    /// <summary>Every <c>run:</c> value of a workflow (inline, or the indented block after <c>run: |</c> / <c>run: &gt;</c>).</summary>
    private static List<RunScript> RunScripts(string yaml)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        var result = new List<RunScript>();
        for (var i = 0; i < lines.Length; i++)
        {
            var m = Regex.Match(lines[i], @"^(\s*)(?:- )?run:\s?(.*)$");
            if (!m.Success)
                continue;
            var value = m.Groups[2].Value.TrimEnd();
            if (Regex.IsMatch(value, @"^[|>][+-]?$"))
            {
                var indent = m.Groups[1].Value.Length;
                var body = new List<string>();
                while (i + 1 < lines.Length && (lines[i + 1].Trim().Length == 0 || lines[i + 1].Length - lines[i + 1].TrimStart().Length > indent))
                    body.Add(lines[++i]);
                result.Add(new RunScript(i + 1 - body.Count, string.Join("\n", body)));
            }
            else
            {
                result.Add(new RunScript(i + 1, value));
            }
        }

        return result;
    }

    /// <summary>A literal backslash-n outside single- or double-quoted text on any line of a run script.</summary>
    internal static List<string> LiteralNewlineProblems(string yaml)
    {
        var problems = new List<string>();
        foreach (var run in RunScripts(yaml))
        {
            foreach (var line in run.Text.Split('\n'))
            {
                var unquoted = Regex.Replace(line, "\"(?:[^\"\\\\]|\\\\.)*\"|'[^']*'", "\"\"");
                if (unquoted.Contains("\\n", StringComparison.Ordinal))
                    problems.Add($"run script near line {run.Line}: literal \\n outside quotes: {line.Trim()}");
            }
        }

        return problems;
    }

    internal static List<string> RestoreWithoutLockedMode(string text)
    {
        var problems = new List<string>();
        foreach (Match m in Regex.Matches(text, @"dotnet restore[^\n;&|]*"))
        {
            if (!m.Value.Contains("--locked-mode", StringComparison.Ordinal))
                problems.Add(m.Value.Trim());
        }

        return problems;
    }

    [Fact]
    public void No_run_script_carries_a_literal_newline_escape()
    {
        var problems = WorkflowFiles()
            .SelectMany(f => LiteralNewlineProblems(System.IO.File.ReadAllText(f)).Select(p => $"{Path.GetFileName(f)}: {p}"))
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Lint_rejects_a_trial_workflow_with_a_literal_newline_in_run()
    {
        // AC-QA-35e negative: the shape that broke e2e-journeys.yml before PLAN-35.
        const string trial = """
            jobs:
              j:
                runs-on: ubuntu-latest
                steps:
                  - name: broken
                    run: |
                      docker compose -f a.yml \n            -f b.yml up -d
                  - name: fine
                    run: printf 'a\nb'
            """;
        var problems = LiteralNewlineProblems(trial);
        Assert.Single(problems);
        Assert.Contains("docker compose -f a.yml", problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_accepts_real_continuations_and_quoted_escapes()
    {
        const string trial = """
            jobs:
              j:
                steps:
                  - run: |
                      docker compose -f a.yml \
                        -f b.yml up -d
                      echo "line1\nline2"
                  - run: >
                      dotnet test x.csproj
                      --filter "A\nB"
            """;
        Assert.Empty(LiteralNewlineProblems(trial));
    }

    [Fact]
    public void Every_dotnet_restore_uses_locked_mode()
    {
        var problems = WorkflowFiles()
            .SelectMany(f => RestoreWithoutLockedMode(System.IO.File.ReadAllText(f)).Select(p => $"{Path.GetFileName(f)}: {p}"))
            .ToList();

        using var steps = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "ci", "steps.json")));
        foreach (var step in steps.RootElement.GetProperty("steps").EnumerateArray())
        {
            problems.AddRange(RestoreWithoutLockedMode(step.GetProperty("run").GetString() ?? "")
                .Select(p => $"steps.json '{step.GetProperty("name").GetString()}': {p}"));
        }

        Assert.True(problems.Count == 0, "dotnet restore without --locked-mode:\n" + string.Join("\n", problems));
        Assert.NotEmpty(RestoreWithoutLockedMode("dotnet restore HRManagement.slnx"));
    }

    [Fact]
    public void Scheduled_jobs_wait_for_ENABLE_SCHEDULED_except_the_dated_obligations_sweep()
    {
        var problems = new List<string>();
        foreach (var file in WorkflowFiles())
        {
            var name = Path.GetFileName(file);
            var text = System.IO.File.ReadAllText(file).Replace("\r\n", "\n");
            if (!Regex.IsMatch(text, @"^\s+schedule:", RegexOptions.Multiline) || name == "dated-obligations.yml")
                continue;

            var lines = text.Split('\n');
            var jobsAt = Array.FindIndex(lines, l => l == "jobs:");
            Assert.True(jobsAt >= 0, $"{name} has no jobs:");
            for (var i = jobsAt + 1; i < lines.Length; i++)
            {
                var job = Regex.Match(lines[i], @"^  ([A-Za-z0-9_-]+):\s*$");
                if (!job.Success)
                    continue;
                var end = Array.FindIndex(lines, i + 1, l => Regex.IsMatch(l, @"^  [A-Za-z0-9_-]+:\s*$"));
                var body = string.Join("\n", lines[(i + 1)..(end < 0 ? lines.Length : end)]);
                var cond = Regex.Match(body, @"^    if:\s*(>-?\s*\n(?:      .*\n?)+|.*)$", RegexOptions.Multiline).Groups[1].Value;
                if (!cond.Contains("vars.ENABLE_SCHEDULED == 'true'", StringComparison.Ordinal)
                    || !cond.Contains("github.event_name != 'schedule'", StringComparison.Ordinal))
                    problems.Add($"{name} job {job.Groups[1].Value}: no ENABLE_SCHEDULED guard (if: {cond.Trim()})");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Duplicate_workflows_stay_deleted_and_codeql_runs_per_PR_only_in_ci()
    {
        Assert.False(System.IO.File.Exists(Path.Combine(WorkflowsDir, "gitleaks.yml")), "gitleaks.yml duplicates the Gitleaks job of ci.yml");
        Assert.False(System.IO.File.Exists(Path.Combine(WorkflowsDir, "gateway-ci.yml")), "gateway-ci.yml duplicates ci.yml");

        var codeql = System.IO.File.ReadAllText(Path.Combine(WorkflowsDir, "codeql.yml")).Replace("\r\n", "\n");
        var on = codeql[codeql.IndexOf("\non:", StringComparison.Ordinal)..codeql.IndexOf("\njobs:", StringComparison.Ordinal)];
        Assert.DoesNotContain("push:", on, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request:", on, StringComparison.Ordinal);

        var ci = System.IO.File.ReadAllText(Path.Combine(WorkflowsDir, "ci.yml")).Replace("\r\n", "\n");
        foreach (var check in new[] { "    name: CI\n", "    name: E2E\n", "    name: CodeQL\n", "    name: Gitleaks\n", "    name: Acceptance\n" })
            Assert.Contains(check, ci, StringComparison.Ordinal);
        foreach (var text in new[] { codeql, ci })
        {
            Assert.DoesNotContain("upload: false", text, StringComparison.Ordinal);
            Assert.Contains("scripts/ci/codeql-sarif-gate.ps1 -Sarif sarif-results", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("gitleaks/gitleaks-action", ci, StringComparison.Ordinal);
        Assert.Contains("bash scripts/ci/gitleaks.sh", ci, StringComparison.Ordinal);
        Assert.Contains("if: vars.ENABLE_ACCEPTANCE_JOB == 'true'", ci, StringComparison.Ordinal);
    }
}
