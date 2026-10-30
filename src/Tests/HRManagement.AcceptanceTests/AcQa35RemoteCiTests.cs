using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-QA-35a–g (PLAN-35): the first push and the remote gates. 35a/35e-negative/35f/35g-negative run the repository's
/// own scripts (pre-push check, actionlint, CodeQL SARIF gate, Dependabot sync); 35b–35e and 35g read the remote runs and
/// the protection through an authenticated <c>gh</c> (<c>GH_TOKEN</c> or <c>gh auth login</c>). Without it the remote
/// scenarios are <c>Blocked</c> (and fail under <c>ACCEPTANCE_REQUIRE_ALL=true</c>), never Pass.
/// </summary>
[RequiredAcceptance]
public class AcQa35RemoteCiTests
{
    private const string Ac = "AC-QA-35";

    private static string Root => AcceptanceHarness.RepoRoot;

    private static string Repo => Environment.GetEnvironmentVariable("ACQA35_REPO") ?? "omardefdev/HRManagement";

    /// <summary>The first-push PR (PLAN-35 §3.3).</summary>
    private static string FirstPushPr => Environment.GetEnvironmentVariable("ACQA35_PR") ?? "24";

    /// <summary>The negative-check PR of AC-QA-35d (branch plan35-negative-check, closed).</summary>
    private static string? NegativePr => Environment.GetEnvironmentVariable("ACQA35_NEGATIVE_PR");

    private static readonly string[] RequiredChecks = ["CI", "CodeQL", "Gitleaks", "E2E"];

    private static EvidenceRecorder Start(string key, string scenario)
    {
        var recorder = new EvidenceRecorder(Ac, $"qa35-{key}-{Guid.NewGuid():N}") { ScenarioKey = key };
        recorder.WriteMetadata(scenario, "n/a (repository scripts and GitHub)");
        AcceptanceGate.RequireEnabled(recorder);
        return recorder;
    }

    // ---------------------------------------------------------------- helpers

    private static (int ExitCode, string Output) Run(string fileName, string arguments, int timeoutMs = 600_000, string? workingDirectory = null,
        IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Root
        };
        foreach (var (k, v) in env ?? new Dictionary<string, string>())
            psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            p.Kill(entireProcessTree: true);
            return (-1, "timeout: " + fileName + " " + arguments);
        }

        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    private static (int ExitCode, string Output) RunScript(string relativeScript, string arguments = "")
    {
        var shell = OperatingSystem.IsWindows() ? "powershell" : "pwsh";
        var script = Path.Combine(Root, relativeScript.Replace('/', Path.DirectorySeparatorChar));
        return Run(shell, $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {arguments}");
    }

    /// <summary>Git Bash on Windows (never the WSL bash.exe of System32), bash elsewhere.</summary>
    private static string Bash =>
        OperatingSystem.IsWindows() && File.Exists(@"C:\Program Files\Git\bin\bash.exe") ? @"C:\Program Files\Git\bin\bash.exe" : "bash";

    private static string Git(string args) => Run("git", args).Output.Trim();

    private static bool GhReady() => Run("gh", "auth status", 60_000).ExitCode == 0;

    private static JsonDocument? GhJson(string args)
    {
        var (exit, output) = Run("gh", args, 120_000);
        if (exit != 0)
            return null;
        var start = output.IndexOfAny(['{', '[']);
        return start < 0 ? null : JsonDocument.Parse(output[start..]);
    }

    private static void RequireGh(EvidenceRecorder r) =>
        AcceptanceGate.RequireData(r, GhReady(),
            "gh غير مصادَق عليه، فلا تُقرأ تشغيلات GitHub",
            "gh is not authenticated (set GH_TOKEN or run gh auth login); the remote runs and the protection cannot be read.");

    private sealed record CheckState(string Name, string Conclusion, string Url);

    private static List<CheckState> Checks(JsonElement rollup) =>
        rollup.EnumerateArray()
            .Where(c => c.TryGetProperty("name", out _))
            .Select(c => new CheckState(
                c.GetProperty("name").GetString() ?? "",
                c.TryGetProperty("conclusion", out var cc) ? cc.GetString() ?? "" : "",
                c.TryGetProperty("detailsUrl", out var u) ? u.GetString() ?? "" : ""))
            .ToList();

    /// <summary>A throwaway repository under the temp folder with one base commit (global.json pinned) tagged "base".</summary>
    private static string TempRepo(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"acqa35-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        Run("git", "init -q -b main", workingDirectory: dir);
        Run("git", "config user.email acqa35@example.invalid", workingDirectory: dir);
        Run("git", "config user.name acqa35", workingDirectory: dir);
        File.WriteAllText(Path.Combine(dir, "global.json"), "{\n  \"sdk\": {\n    \"version\": \"10.0.400\",\n    \"rollForward\": \"disable\"\n  }\n}\n");
        File.WriteAllText(Path.Combine(dir, "README.md"), "acqa35\n");
        Run("git", "add .", workingDirectory: dir);
        Run("git", "commit -qm base", workingDirectory: dir);
        Run("git", "tag base", workingDirectory: dir);
        return dir;
    }

    private static void Commit(string dir, string message)
    {
        Run("git", "add .", workingDirectory: dir);
        Run("git", $"commit -qm \"{message}\"", workingDirectory: dir);
    }

    // ---------------------------------------------------------------- 35a

    [SkippableFact]
    public void Ac35_a_prepush_check_is_green_on_head_and_red_on_a_secret_or_a_global_json_change()
    {
        var r = Start("35a", "AC-QA-35a: scripts/plan035-prepush.ps1 passes on HEAD (gitleaks whole history + range, actionlint, forbidden files, GOV-001-D, ci-local summary); a planted secret or a global.json rollForward change in a temp repository exits 1 naming the file");

        var head = Git("rev-parse HEAD");
        var (fastExit, fastOut) = RunScript("scripts/plan035-prepush.ps1", "-Only gitleaks,actionlint,forbidden,nonprod,signatures -Out artifacts/plan35/acqa35a");

        var summaryPath = Path.Combine(Root, "artifacts", "ci-local", "summary.json");
        AcceptanceGate.RequireData(r, File.Exists(summaryPath),
            "ملخص ci-local غير موجود", "artifacts/ci-local/summary.json is missing: run powershell -File scripts/ci-local.ps1 -SkipTestcontainers");
        using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
        var summarySha = summary.RootElement.GetProperty("gitSha").GetString()!;
        var (ciExit, ciOut) = RunScript("scripts/plan035-prepush.ps1", $"-Only cilocal -Head {summarySha} -Out artifacts/plan35/acqa35a-cilocal");
        var summaryIsAncestor = Run("git", $"merge-base --is-ancestor {summarySha} HEAD").ExitCode == 0;

        // Negative 1: a random GitHub token (generated here, so this source file never carries one).
        var secretRepo = TempRepo("secret");
        var token = "ghp_" + string.Concat(Enumerable.Range(0, 36).Select(_ => "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789"[Random.Shared.Next(57)]));
        File.WriteAllText(Path.Combine(secretRepo, "deploy.env.sample"), $"GITHUB_TOKEN={token}\n");
        Commit(secretRepo, "planted token");
        var (secretExit, secretOut) = RunScript("scripts/plan035-prepush.ps1",
            $"-RepoRoot \"{secretRepo}\" -Base base -Only gitleaks,forbidden -Out \"{Path.Combine(secretRepo, "..", Path.GetFileName(secretRepo) + "-out")}\"");

        // Negative 2: global.json rollForward changed in the range.
        var gjRepo = TempRepo("globaljson");
        File.WriteAllText(Path.Combine(gjRepo, "global.json"), "{\n  \"sdk\": {\n    \"version\": \"10.0.400\",\n    \"rollForward\": \"latestPatch\"\n  }\n}\n");
        Commit(gjRepo, "roll forward");
        var (gjExit, gjOut) = RunScript("scripts/plan035-prepush.ps1",
            $"-RepoRoot \"{gjRepo}\" -Base base -GlobalJsonPinCommit base -Only gitleaks,forbidden -Out \"{Path.Combine(gjRepo, "..", Path.GetFileName(gjRepo) + "-out")}\"");

        r.WriteScenarioSteps("""
            1. powershell -File scripts/plan035-prepush.ps1 -Only gitleaks,actionlint,forbidden,nonprod,signatures on HEAD → exit 0.
            2. The ci-local summary (artifacts/ci-local/summary.json) passes the cilocal check for its own commit, and that commit
               is an ancestor of HEAD (the pushed release head).
            3. Negative: temp repository with a random ghp_ token committed → exit 1, the finding names deploy.env.sample.
            4. Negative: temp repository whose range sets global.json rollForward latestPatch → exit 1 naming global.json.
            """);
        r.WriteScenarioDbChecks(new
        {
            head,
            fast = new { exit = fastExit, lines = fastOut.Split('\n').Where(l => l.StartsWith('[') || l.Contains("leaks", StringComparison.Ordinal)).Select(l => l.Trim()).ToArray() },
            cilocal = new { summarySha, exit = ciExit, summaryIsAncestor },
            secret = new { exit = secretExit, namesFile = secretOut.Contains("deploy.env.sample", StringComparison.Ordinal), rule = secretOut.Contains("rule=github-pat", StringComparison.Ordinal) },
            globalJson = new { exit = gjExit, namesFile = gjOut.Contains("FORBIDDEN: global.json", StringComparison.Ordinal) }
        });
        Assert.True(fastExit == 0, fastOut);
        Assert.True(ciExit == 0, ciOut);
        Assert.True(summaryIsAncestor, $"ci-local summary {summarySha} is not an ancestor of HEAD {head}");
        Assert.Equal(1, secretExit);
        Assert.Contains("deploy.env.sample", secretOut, StringComparison.Ordinal);
        Assert.Equal(1, gjExit);
        Assert.Contains("FORBIDDEN: global.json", gjOut, StringComparison.Ordinal);
        r.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 35b

    [SkippableFact]
    public void Ac35_b_first_push_pr_has_green_required_checks()
    {
        var r = Start("35b", "AC-QA-35b: the first-push PR shows CI, CodeQL, Gitleaks and E2E green on its last head");
        RequireGh(r);
        using var pr = GhJson($"pr view {FirstPushPr} -R {Repo} --json number,url,state,headRefName,headRefOid,mergedAt,statusCheckRollup");
        AcceptanceGate.RequireData(r, pr is not null, "تعذرت قراءة الـ PR", $"gh pr view {FirstPushPr} failed");
        var root = pr!.RootElement;
        var checks = Checks(root.GetProperty("statusCheckRollup"));
        r.WriteScenarioSteps($"""
            1. gh pr view {FirstPushPr} --json statusCheckRollup on {Repo}.
            2. CI, CodeQL, Gitleaks and E2E (ci.yml job names) concluded SUCCESS on the PR's head commit.
            """);
        r.WriteScenarioDbChecks(new
        {
            pr = root.GetProperty("url").GetString(),
            state = root.GetProperty("state").GetString(),
            head = root.GetProperty("headRefOid").GetString(),
            checks = checks.Select(c => new { c.Name, c.Conclusion, c.Url })
        });
        foreach (var name in RequiredChecks)
        {
            var c = checks.LastOrDefault(x => x.Name == name);
            Assert.True(c is not null, $"check {name} missing on the PR");
            Assert.Equal("SUCCESS", c!.Conclusion);
        }

        r.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 35c

    [SkippableFact]
    public void Ac35_c_push_run_on_main_at_the_merged_sha_is_green()
    {
        var r = Start("35c", "AC-QA-35c: the push run of ci.yml on main at the SHA of origin/main is green for every enabled job; remote main = local main");
        RequireGh(r);
        Run("git", "fetch -q origin main");
        var remoteMain = Git("rev-parse origin/main");
        var localMain = Git("rev-parse main");
        using var runs = GhJson($"run list -R {Repo} --workflow ci.yml --branch main --event push --limit 5 --json databaseId,headSha,conclusion,url,createdAt,updatedAt");
        AcceptanceGate.RequireData(r, runs is not null, "تعذرت قراءة التشغيلات", "gh run list failed");
        var run = runs!.RootElement.EnumerateArray().FirstOrDefault(x => x.GetProperty("headSha").GetString() == remoteMain);
        AcceptanceGate.RequireData(r, run.ValueKind == JsonValueKind.Object, "لا تشغيل push على main بهذا الـ SHA", $"no ci.yml push run on main for {remoteMain}");
        var id = run.GetProperty("databaseId").GetInt64();
        using var jobs = GhJson($"run view {id} -R {Repo} --json jobs");
        var jobList = jobs!.RootElement.GetProperty("jobs").EnumerateArray()
            .Select(j => new { name = j.GetProperty("name").GetString(), conclusion = j.GetProperty("conclusion").GetString() })
            .ToList();
        r.WriteScenarioSteps("""
            1. git fetch origin main; origin/main must equal local main.
            2. gh run list --workflow ci.yml --branch main --event push: the run whose headSha is origin/main.
            3. Every job of that run concluded success, except the Acceptance job, skipped while ENABLE_ACCEPTANCE_JOB is unset
               (obligation CI-ACCEPTANCE-DISABLED).
            """);
        r.WriteScenarioDbChecks(new { remoteMain, localMain, run = run.GetProperty("url").GetString(), conclusion = run.GetProperty("conclusion").GetString(), jobs = jobList });
        Assert.Equal(localMain, remoteMain);
        Assert.Equal("success", run.GetProperty("conclusion").GetString());
        foreach (var j in jobList)
        {
            if (j.name == "Acceptance")
                Assert.Equal("skipped", j.conclusion);
            else
                Assert.True(j.conclusion == "success", $"job {j.name}: {j.conclusion}");
        }

        foreach (var name in RequiredChecks)
            Assert.Contains(jobList, j => j.name == name);
        r.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 35d

    [SkippableFact]
    public void Ac35_d_live_protection_matches_the_file_and_a_red_change_cannot_reach_main()
    {
        var r = Start("35d", "AC-QA-35d: scm001-branch-protection-validate.ps1 -CheckLiveGitHub passes (live protection = file, or the D-QA-1 local guard where GitHub cannot protect); a direct push to main is refused; the negative-check PR is red and closed");
        RequireGh(r);
        var (liveExit, liveOut) = RunScript("scripts/scm001-branch-protection-validate.ps1", "-CheckLiveGitHub");
        var head = Git("rev-parse HEAD");
        var (guardExit, guardOut) = RunScript("scripts/scm/pre-push-guard.ps1",
            $"origin x -ProtectMain -SelfTestRefs \"refs/heads/main {head} refs/heads/main {Git("rev-parse origin/main")}\"");

        AcceptanceGate.RequireData(r, !string.IsNullOrWhiteSpace(NegativePr), "رقم PR الفحص السلبي غير مضبوط",
            "Set ACQA35_NEGATIVE_PR to the number of the plan35-negative-check PR.");
        using var neg = GhJson($"pr view {NegativePr} -R {Repo} --json number,url,state,headRefName,mergeStateStatus,mergedAt,statusCheckRollup");
        AcceptanceGate.RequireData(r, neg is not null, "تعذرت قراءة PR الفحص السلبي", $"gh pr view {NegativePr} failed");
        var negRoot = neg!.RootElement;
        var negChecks = Checks(negRoot.GetProperty("statusCheckRollup"));
        r.WriteScenarioSteps("""
            1. powershell -File scripts/scm001-branch-protection-validate.ps1 -CheckLiveGitHub: the live protection equals the
               file, or (private repository on the free plan, 403 "Upgrade to GitHub Pro") the open exception
               SCM-NO-GITHUB-PROTECTION plus the installed pre-push guard with -ProtectMain.
            2. Negative: the guard refuses a push of HEAD to refs/heads/main (exit 1).
            3. Negative: PR from plan35-negative-check with a deliberately red unit test: its CI check failed, the PR was closed
               without merge (mergeStateStatus recorded; BLOCKED only where GitHub enforces protection).
            """);
        r.WriteScenarioDbChecks(new
        {
            live = new { exit = liveExit, output = liveOut.Trim() },
            guard = new { exit = guardExit, refused = guardOut.Contains("REFUSED direct push to main", StringComparison.Ordinal) },
            negativePr = new
            {
                url = negRoot.GetProperty("url").GetString(),
                state = negRoot.GetProperty("state").GetString(),
                head = negRoot.GetProperty("headRefName").GetString(),
                mergeStateStatus = negRoot.GetProperty("mergeStateStatus").GetString(),
                checks = negChecks.Select(c => new { c.Name, c.Conclusion })
            }
        });
        Assert.True(liveExit == 0, liveOut);
        Assert.Equal(1, guardExit);
        Assert.Contains("REFUSED direct push to main", guardOut, StringComparison.Ordinal);
        Assert.Equal("plan35-negative-check", negRoot.GetProperty("headRefName").GetString());
        Assert.Equal("CLOSED", negRoot.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, negRoot.GetProperty("mergedAt").ValueKind);
        Assert.Contains(negChecks, c => c.Name == "CI" && c.Conclusion == "FAILURE");
        r.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 35e

    [SkippableFact]
    public void Ac35_e_manual_journeys_run_passes_both_profiles_and_a_literal_newline_fails_lint()
    {
        var r = Start("35e", "AC-QA-35e: a workflow_dispatch run of e2e-journeys.yml on main passes; recruitment-journey prints 'Passed: 1, skipped: 0' for the lab and the prod-like profile; a workflow with a literal \\n in run fails actionlint");

        // Negative (local): actionlint (pinned) on a trial workflow carrying the pre-PLAN-35 literal "\n".
        var trialDir = Path.Combine(Path.GetTempPath(), $"acqa35-lint-{Guid.NewGuid():N}", ".github", "workflows");
        Directory.CreateDirectory(trialDir);
        File.WriteAllText(Path.Combine(trialDir, "trial.yml"),
            "on: workflow_dispatch\njobs:\n  j:\n    runs-on: ubuntu-latest\n    steps:\n      - run: |\n          docker compose -f a.yml \\n            -f b.yml up -d\n");
        using var pins = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs", "qa", "tool-pins.json")));
        var actionlint = pins.RootElement.GetProperty("tools").GetProperty("actionlint").GetProperty("ref").GetString();
        var trialRoot = Path.GetDirectoryName(Path.GetDirectoryName(trialDir))!;
        var (lintExit, lintOut) = Run("docker", $"run --rm -v \"{trialRoot}:/repo\" -w /repo {actionlint} -no-color -oneline .github/workflows/trial.yml");

        RequireGh(r);
        using var runs = GhJson($"run list -R {Repo} --workflow e2e-journeys.yml --branch main --event workflow_dispatch --limit 5 --json databaseId,headSha,conclusion,url");
        var run = runs?.RootElement.EnumerateArray().FirstOrDefault(x => x.GetProperty("conclusion").GetString() == "success") ?? default;
        AcceptanceGate.RequireData(r, run.ValueKind == JsonValueKind.Object, "لا تشغيل يدوي ناجح لـ e2e-journeys على main",
            "no successful workflow_dispatch run of e2e-journeys.yml on main");
        var id = run.GetProperty("databaseId").GetInt64();
        var (logExit, log) = Run("gh", $"run view {id} -R {Repo} --log", 300_000);
        var passedLines = log.Split('\n').Where(l => l.Contains("recruitment-journey", StringComparison.Ordinal) && l.Contains("Passed: 1, skipped: 0", StringComparison.Ordinal)).ToList();

        r.WriteScenarioSteps("""
            1. Negative: actionlint (docs/qa/tool-pins.json) on a trial workflow whose run step carries a literal "\n" → exit 1,
               shellcheck SC1012 (WorkflowYamlLintTests checks the same shape in CI).
            2. gh run list --workflow e2e-journeys.yml --branch main --event workflow_dispatch: a successful run.
            3. Its recruitment-journey job log prints "Passed: 1, skipped: 0" twice (lab profile, then prod-like profile).
            """);
        r.WriteScenarioDbChecks(new
        {
            lint = new { exit = lintExit, sc1012 = lintOut.Contains("SC1012", StringComparison.Ordinal) },
            run = run.GetProperty("url").GetString(),
            headSha = run.GetProperty("headSha").GetString(),
            logExit,
            passedLines = passedLines.Select(l => l.Trim()).ToArray()
        });
        Assert.NotEqual(0, lintExit);
        Assert.Contains("SC1012", lintOut, StringComparison.Ordinal);
        Assert.Equal(0, logExit);
        Assert.Equal(2, passedLines.Count);
        r.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 35f

    [SkippableFact]
    public void Ac35_f_codeql_gate_passes_on_the_baseline_and_fails_on_a_new_high_or_expired_entry()
    {
        var r = Start("35f", "AC-QA-35f: scripts/ci/codeql-sarif-gate.ps1 passes on SARIF matching the baseline; fails on a new security-severity 8.1 result or an expired baseline entry");
        const string fx = "testdata/qa/codeql-gate";
        var (okExit, okOut) = RunScript("scripts/ci/codeql-sarif-gate.ps1", $"-Sarif {fx}/run.sarif -Baseline {fx}/baseline-ok.json -AsOfDate 2026-11-01");
        var (newExit, newOut) = RunScript("scripts/ci/codeql-sarif-gate.ps1", $"-Sarif {fx}/new-high.sarif -Baseline {fx}/baseline-ok.json -AsOfDate 2026-11-01");
        var (expExit, expOut) = RunScript("scripts/ci/codeql-sarif-gate.ps1", $"-Sarif {fx}/run.sarif -Baseline {fx}/baseline-expired.json -AsOfDate 2026-11-01");
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs", "qa", "codeql-baseline.json")));
        var entries = baseline.RootElement.GetProperty("entries").GetArrayLength();
        r.WriteScenarioSteps("""
            1. Gate on run.sarif with a baseline that lists its only high result (Open, review 2027-01-01) → exit 0.
            2. Negative: new-high.sarif (cs/sql-injection, security-severity 8.1, a path not in the baseline) → exit 1.
            3. Negative: the same baseline entry with reviewBy 2026-10-01 → exit 1 (expired).
            4. The live baseline docs/qa/codeql-baseline.json lists the pre-existing alerts one by one (rule, path, fingerprint, owner, reviewBy).
            """);
        r.WriteScenarioDbChecks(new { ok = okExit, newHigh = newExit, expired = expExit, liveBaselineEntries = entries });
        Assert.True(okExit == 0, okOut);
        Assert.Equal(1, newExit);
        Assert.Contains("security-severity 8.1", newOut, StringComparison.Ordinal);
        Assert.Equal(1, expExit);
        Assert.Contains("expired on 2026-10-01", expOut, StringComparison.Ordinal);
        r.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 35g

    [SkippableFact]
    public void Ac35_g_dated_obligations_runs_on_main_and_the_dependabot_sync_fails_loudly()
    {
        var r = Start("35g", "AC-QA-35g: dated-obligations.yml runs green by hand on main; the Dependabot sync script fails with an explicit message on a refused token");
        var (depExit, depOut) = Run(Bash, "scripts/sync-dependabot-alerts-to-issues.sh", 120_000,
            env: new Dictionary<string, string> { ["GH_TOKEN"] = "acqa35-invalid-token", ["GITHUB_REPOSITORY"] = Repo });

        RequireGh(r);
        using var runs = GhJson($"run list -R {Repo} --workflow dated-obligations.yml --branch main --event workflow_dispatch --limit 5 --json databaseId,headSha,conclusion,url");
        var run = runs?.RootElement.EnumerateArray().FirstOrDefault(x => x.GetProperty("conclusion").GetString() == "success") ?? default;
        AcceptanceGate.RequireData(r, run.ValueKind == JsonValueKind.Object, "لا تشغيل يدوي ناجح لـ dated-obligations على main",
            "no successful workflow_dispatch run of dated-obligations.yml on main");
        r.WriteScenarioSteps("""
            1. gh run list --workflow dated-obligations.yml --branch main --event workflow_dispatch: a successful run.
            2. Negative: bash scripts/sync-dependabot-alerts-to-issues.sh with an invalid GH_TOKEN → exit 1, "refused" and the
               missing-permission message; never "Open Dependabot alerts: 0".
            """);
        r.WriteScenarioDbChecks(new
        {
            run = run.GetProperty("url").GetString(),
            headSha = run.GetProperty("headSha").GetString(),
            dependabot = new { exit = depExit, output = depOut.Trim() }
        });
        Assert.Equal(1, depExit);
        Assert.Contains("refused", depOut, StringComparison.Ordinal);
        Assert.Contains("Dependabot alerts: read", depOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Open Dependabot alerts: 0", depOut, StringComparison.Ordinal);
        r.WriteResult("Pass");
    }
}
