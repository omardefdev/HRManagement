using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// QA-034: <c>scripts/ci/steps.json</c> is the single source of the CI build-and-test steps. The
/// <c>build-and-test</c> job of <c>.github/workflows/ci.yml</c> must list exactly those steps, in the same order, each
/// as <c>bash scripts/ci/run-steps.sh --step "&lt;name&gt;"</c>; only checkout, setup-dotnet and the evidence upload may
/// be <c>uses:</c> steps. scripts/ci-local.ps1 runs the same list locally, so local and remote CI cannot drift.
/// </summary>
public class CiStepsParityTests
{
    private static readonly string[] AllowedActions =
        ["actions/checkout@", "actions/setup-dotnet@", "actions/upload-artifact@"];

    private sealed record WorkflowStep(string? Name, string? Uses, string? Run, string? If);

    private static IReadOnlyList<WorkflowStep> BuildAndTestSteps()
    {
        var lines = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml"))
            .Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l == "  build-and-test:");
        Assert.True(start >= 0, "ci.yml has no build-and-test job");
        var end = Array.FindIndex(lines, start + 1, l => Regex.IsMatch(l, @"^  [A-Za-z0-9_-]+:\s*$"));
        if (end < 0) end = lines.Length;

        var steps = new List<WorkflowStep>();
        string? name = null, uses = null, run = null, cond = null;
        var open = false;
        void Flush()
        {
            if (open) steps.Add(new WorkflowStep(name, uses, run, cond));
            name = uses = run = cond = null;
            open = false;
        }

        for (var i = start; i < end; i++)
        {
            var l = lines[i];
            Match m;
            if ((m = Regex.Match(l, @"^      - (name|uses|run): ?(.*)$")).Success)
            {
                Flush();
                open = true;
                Assign(m.Groups[1].Value, m.Groups[2].Value);
                continue;
            }

            if (open && (m = Regex.Match(l, @"^        (name|uses|run|if): ?(.*)$")).Success)
            {
                Assign(m.Groups[1].Value, m.Groups[2].Value);
                if (m.Groups[1].Value == "run" && (m.Groups[2].Value is "|" or ">" or ""))
                {
                    // multi-line run: collect it so the test can report it as an inline script
                    var body = new List<string>();
                    while (i + 1 < end && (lines[i + 1].StartsWith("          ", StringComparison.Ordinal) || lines[i + 1].Trim().Length == 0))
                        body.Add(lines[++i].Trim());
                    run = string.Join("\n", body).Trim();
                }
            }
        }

        Flush();
        return steps;

        void Assign(string key, string value)
        {
            value = value.Trim();
            switch (key)
            {
                case "name": name = value; break;
                case "uses": uses = value; break;
                case "run": run = value; break;
                case "if": cond = value; break;
            }
        }
    }

    private static List<JsonElement> StepsJson()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "ci", "steps.json")));
        return doc.RootElement.GetProperty("steps").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    [Fact]
    public void Ci_build_and_test_runs_exactly_the_steps_json_list_in_order()
    {
        var workflow = BuildAndTestSteps();
        var runSteps = workflow.Where(s => s.Uses is null).ToList();
        var expected = StepsJson().Select(s => s.GetProperty("name").GetString()!).ToList();

        Assert.Equal(expected, runSteps.Select(s => s.Name ?? "<unnamed>").ToList());
    }

    [Fact]
    public void Ci_build_and_test_has_no_inline_script()
    {
        var problems = new List<string>();
        foreach (var s in BuildAndTestSteps())
        {
            if (s.Uses is not null)
            {
                if (!AllowedActions.Any(a => s.Uses.StartsWith(a, StringComparison.Ordinal)))
                    problems.Add($"uses: {s.Uses} (only checkout, setup-dotnet and upload-artifact may be actions)");
                continue;
            }

            var expectedRun = $"bash scripts/ci/run-steps.sh --step \"{s.Name}\"";
            if (!string.Equals(s.Run, expectedRun, StringComparison.Ordinal))
                problems.Add($"step '{s.Name}': run must be `{expectedRun}`, found `{s.Run}`");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // Review A-1: keys such as continue-on-error, env, shell, working-directory or timeout-minutes on a ci.yml step would
    // make the remote run differ from scripts/ci/steps.json (a red step hidden by continue-on-error, say).
    [Fact]
    public void Ci_build_and_test_steps_carry_no_other_keys()
    {
        var lines = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml"))
            .Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l == "  build-and-test:");
        var end = Array.FindIndex(lines, start + 1, l => Regex.IsMatch(l, @"^  [A-Za-z0-9_-]+:\s*$"));
        if (end < 0) end = lines.Length;

        var problems = new List<string>();
        var isAction = false;
        for (var i = start; i < end; i++)
        {
            Match m;
            // job-level keys: defaults, env, continue-on-error, strategy and the like would also change every step
            if ((m = Regex.Match(lines[i], @"^    ([A-Za-z_-]+):")).Success && m.Groups[1].Value is not ("name" or "runs-on" or "steps"))
            {
                problems.Add($"ci.yml:{i + 1}: job key '{m.Groups[1].Value}' is not allowed on build-and-test");
                continue;
            }

            if ((m = Regex.Match(lines[i], @"^      - ([A-Za-z_-]+):")).Success)
            {
                isAction = m.Groups[1].Value == "uses";
                if (m.Groups[1].Value is not ("name" or "uses"))
                    problems.Add($"ci.yml:{i + 1}: a step starts with '{m.Groups[1].Value}'");
                continue;
            }

            if ((m = Regex.Match(lines[i], @"^        ([A-Za-z_-]+):")).Success)
            {
                var key = m.Groups[1].Value;
                if (key == "uses") isAction = true;
                var allowed = key is "name" or "run" or "if" || (isAction && key is "uses" or "with");
                if (!allowed)
                    problems.Add($"ci.yml:{i + 1}: step key '{key}' is not allowed in build-and-test (put the behaviour in scripts/ci/steps.json)");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Always_steps_match_if_always_in_ci()
    {
        var workflow = BuildAndTestSteps().Where(s => s.Uses is null).ToDictionary(s => s.Name!);
        foreach (var step in StepsJson())
        {
            var name = step.GetProperty("name").GetString()!;
            var always = step.TryGetProperty("always", out var a) && a.GetBoolean();
            Assert.True(workflow.ContainsKey(name), $"missing in ci.yml: {name}");
            Assert.True(always == (workflow[name].If == "always()"),
                $"{name}: steps.json always={always} but ci.yml if='{workflow[name].If}'");
        }
    }

    [Fact]
    public void Steps_json_entries_are_well_formed()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in StepsJson())
        {
            var name = step.GetProperty("name").GetString()!;
            Assert.True(names.Add(name), $"duplicate step name {name}");
            Assert.DoesNotContain('"', name);
            Assert.Contains(step.GetProperty("kind").GetString(), new[] { "guard", "build", "test", "gate" });
            Assert.False(string.IsNullOrWhiteSpace(step.GetProperty("run").GetString()), $"{name}: empty run");
        }

        // the horizon check must run before the date-sensitive STR/MIG runtime validators
        var order = StepsJson().Select(s => s.GetProperty("name").GetString()!).ToList();
        var horizon = order.IndexOf("Dated obligations horizon (QA-034)");
        Assert.True(horizon >= 0, "Dated obligations horizon (QA-034) step missing");
        Assert.True(order.FindIndex(n => n.StartsWith("STR-", StringComparison.Ordinal) || n.StartsWith("MIG-", StringComparison.Ordinal)) > horizon);
    }

    // Rule: no unowned exclusion. Every Category!= filter in a test step (other than the InMemory/Testcontainers
    // split) is an entry in tests/ci-exclusions.json, which is a dated obligation (owner + review date).
    [Fact]
    public void Every_excluded_test_category_is_a_registered_exclusion()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "tests", "ci-exclusions.json")));
        var registered = doc.RootElement.GetProperty("exclusions").EnumerateArray()
            .Where(e => e.GetProperty("status").GetString() == "Excluded")
            .ToDictionary(e => e.GetProperty("category").GetString()!, e => e);

        var excluded = StepsJson()
            .SelectMany(s => Regex.Matches(s.GetProperty("run").GetString()!, @"Category!=([A-Za-z0-9_]+)").Select(m => m.Groups[1].Value))
            .Where(c => c != "Testcontainers")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var category in excluded)
        {
            Assert.True(registered.ContainsKey(category), $"Category!={category} in scripts/ci/steps.json has no entry in tests/ci-exclusions.json");
            var e = registered[category];
            Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("owner").GetString()));
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", e.GetProperty("reviewBy").GetString()!);
        }

        foreach (var category in registered.Keys)
            Assert.True(excluded.Contains(category), $"tests/ci-exclusions.json lists {category} but no CI step excludes it (stale entry)");
    }
}
