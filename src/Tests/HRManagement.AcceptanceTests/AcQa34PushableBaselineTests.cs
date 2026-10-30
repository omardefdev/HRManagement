using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using HRManagement.ServiceDefaults.ProgramGovernance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-QA-34a–i (PLAN-34): <c>main</c> is pushable. Most scenarios are proven on repository evidence, not on a live
/// stack: the clean-HEAD Linux run of <c>scripts/ci-local.ps1</c> (<c>summary.json</c> + its trx files), the Windows/ar-SY
/// trx files, the dated-obligations script, the governance evaluator and a vulnerable-package restore probe. 34g needs
/// the base compose stack without MinIO (<c>ACQA34_BASE_STACK_URL</c>). Missing evidence is <c>Blocked</c> (and fails under
/// <c>ACCEPTANCE_REQUIRE_ALL=true</c>), never Pass.
/// </summary>
[RequiredAcceptance]
public class AcQa34PushableBaselineTests
{
    private const string Ac = "AC-QA-34";

    private static string Root => AcceptanceHarness.RepoRoot;

    /// <summary>The clean-HEAD Linux CI run (default <c>artifacts/ci-local/head</c>).</summary>
    private static string LinuxRun =>
        Path.Combine(Root, (Environment.GetEnvironmentVariable("ACQA34_CI_LOCAL_DIR") ?? "artifacts/ci-local/head").Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The Windows/ar-SA trx files of the same HEAD (default <c>artifacts/plan34/windows</c>).</summary>
    private static string WindowsRun =>
        Path.Combine(Root, (Environment.GetEnvironmentVariable("ACQA34_WINDOWS_RESULTS_DIR") ?? "artifacts/plan34/windows").Replace('/', Path.DirectorySeparatorChar));

    private static EvidenceRecorder Start(string key, string scenario)
    {
        var recorder = new EvidenceRecorder(Ac, $"qa34-{key}-{Guid.NewGuid():N}") { ScenarioKey = key };
        recorder.WriteMetadata(scenario, "n/a (repository and CI evidence)");
        AcceptanceGate.RequireEnabled(recorder);
        return recorder;
    }

    // ---------------------------------------------------------------- helpers

    private static (int ExitCode, string Output) Run(string fileName, string arguments, int timeoutMs = 600_000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Root
        };
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

    private static string GitHead() => Run("git", "rev-parse HEAD").Output.Trim();

    private static JsonObject? LoadSummary()
    {
        var path = Path.Combine(LinuxRun, "summary.json");
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : null;
    }

    private const string TestcontainersStep = "Integration tests (Testcontainers)";

    /// <summary>
    /// The evidence rule of plan §1 rule 3: summary of HEAD, nothing dirty, every step of steps.json passed. Rule P-3:
    /// only the Testcontainers step may be "not-run" (-SkipTestcontainers), and only when <paramref name="nativeTestcontainersGreen"/>
    /// (the native run of the same HEAD) is true.
    /// </summary>
    internal static List<string> SummaryProblems(JsonObject summary, string head, bool nativeTestcontainersGreen = false)
    {
        var problems = new List<string>();
        if ((string?)summary["gitSha"] != head)
            problems.Add($"summary gitSha {(string?)summary["gitSha"]} is not HEAD {head}");
        if (summary["dirtyTracked"] is JsonArray dirty && dirty.Count > 0)
            problems.Add("dirtyTracked is not empty: " + string.Join(", ", dirty.Select(d => (string?)d)));
        foreach (var o in summary["knownLocalOverrides"] as JsonArray ?? [])
        {
            if (!((string?)o ?? "").StartsWith("global.json", StringComparison.Ordinal))
                problems.Add($"unexpected local override: {(string?)o}");
        }

        var notRunAllowed = 0;
        foreach (var step in summary["steps"] as JsonArray ?? [])
        {
            var status = (string?)step!["status"];
            if (status == "passed")
                continue;
            if (status == "not-run" && (string?)step["name"] == TestcontainersStep && nativeTestcontainersGreen)
            {
                notRunAllowed++;
                continue;
            }
            problems.Add($"step '{(string?)step["name"]}' is {status} (exit {step["exit"]})");
        }

        if (summary["complete"]?.GetValue<bool>() != true && notRunAllowed == 0)
            problems.Add("summary.complete is not true");
        return problems;
    }

    private sealed record TrxResult(string Name, string Outcome);

    private static List<TrxResult> LoadTrx(string folder, string fileName)
    {
        var file = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, fileName, SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (file is null)
            return [];
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        return XDocument.Load(file).Descendants(ns + "UnitTestResult")
            .Select(r => new TrxResult((string?)r.Attribute("testName") ?? "", (string?)r.Attribute("outcome") ?? ""))
            .ToList();
    }

    private static (int Total, int Passed, int Failed) Count(IEnumerable<TrxResult> results, string contains)
    {
        var hits = results.Where(r => r.Name.Contains(contains, StringComparison.Ordinal)).ToList();
        return (hits.Count, hits.Count(r => r.Outcome == "Passed"), hits.Count(r => r.Outcome == "Failed"));
    }

    private static List<TrxResult> LinuxTrx(string file) => LoadTrx(Path.Combine(LinuxRun, "TestResults"), file);

    /// <summary>Testcontainers results: the Linux run's, or (rule P-3, -SkipTestcontainers) the native run of the same HEAD.</summary>
    private static (List<TrxResult> Results, string Source) TestcontainersTrx()
    {
        var linux = LinuxTrx("integration-tc-results.trx");
        return linux.Count > 0 ? (linux, "linux ci-local") : (LoadTrx(WindowsRun, "tc.trx"), "native Windows (rule P-3)");
    }

    private static bool NativeTestcontainersGreen()
    {
        var native = LoadTrx(WindowsRun, "tc.trx");
        return native.Count > 0 && native.All(r => r.Outcome is "Passed")
               && native.Any(r => r.Name.Contains("Plan034ScopedIndexPostgreSqlTests", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- 34a

    [SkippableFact]
    public void Ac34_a_local_linux_ci_is_green_on_a_clean_head()
    {
        var r = Start("34a", "AC-QA-34a: scripts/ci-local.ps1 on a clean HEAD — every step exits 0, summary of HEAD, nothing dirty");
        var summary = LoadSummary();
        AcceptanceGate.RequireData(r, summary is not null,
            "لا يوجد summary.json لتشغيل CI المحلي", $"No summary.json in {LinuxRun}; run: powershell -File scripts/ci-local.ps1 -Out artifacts/ci-local/head");
        var head = GitHead();
        var nativeTc = NativeTestcontainersGreen();
        var problems = SummaryProblems(summary!, head, nativeTc);

        // Negative: the same rule refuses a summary of another commit and a summary with a failed step.
        var otherSha = JsonNode.Parse(summary!.ToJsonString())!.AsObject();
        otherSha["gitSha"] = "0000000000000000000000000000000000000000";
        var failedStep = JsonNode.Parse(summary.ToJsonString())!.AsObject();
        failedStep["steps"]![0]!["status"] = "failed";
        var otherShaRejected = SummaryProblems(otherSha, head).Any(p => p.Contains("is not HEAD", StringComparison.Ordinal));
        var failedStepRejected = SummaryProblems(failedStep, head).Any(p => p.Contains("is failed", StringComparison.Ordinal));

        r.WriteScenarioSteps($"""
            1. Read {Path.GetRelativePath(Root, LinuxRun)}/summary.json written by run-steps.sh inside the ci-local container.
            2. Rule: gitSha == `git rev-parse HEAD` ({head}), dirtyTracked empty, only a global.json rollForward override, every
               step of scripts/ci/steps.json passed, complete = true.
            3. Negative: the rule rejects the same summary with another gitSha and with one failed step.
            4. Rule P-3: on Docker Desktop, Testcontainers inside the ci-local container times out intermittently through the
               desktop port proxy, so the evidence run uses -SkipTestcontainers (the step is "not-run", never dropped) and the
               Testcontainers suite runs natively on Windows for the same HEAD (artifacts/plan34/windows/tc.trx, 0 failures).
            """);
        r.WriteScenarioDbChecks(new
        {
            head,
            summaryGitSha = (string?)summary["gitSha"],
            source = (string?)summary["source"],
            counts = summary["counts"],
            knownLocalOverrides = summary["knownLocalOverrides"],
            totalSeconds = (summary["steps"] as JsonArray ?? []).Sum(s => s!["durationSec"]?.GetValue<double>() ?? 0),
            trx = summary["trx"],
            nativeTestcontainersGreen = nativeTc,
            rewrittenByRunCount = summary["rewrittenByRunCount"],
            problems,
            negative = new { otherShaRejected, failedStepRejected }
        });
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(otherShaRejected);
        Assert.True(failedStepRejected);
        r.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 34b

    [SkippableFact]
    public void Ac34_b_dated_obligations_horizon_passes_today_and_fails_near_expiry()
    {
        var r = Start("34b", "AC-QA-34b: dated-obligations-check.ps1 — today exit 0 with the nearest obligation; 5 days → 1; 20 days → warning; Declared expired → 1");
        var (realExit, realOut) = RunScript("scripts/qa/dated-obligations-check.ps1", "-OutFile artifacts/acqa34-dated-obligations.json");
        var nearest = realOut.Split('\n').FirstOrDefault(l => l.StartsWith("Nearest open obligation:", StringComparison.Ordinal))?.Trim();
        var (fail5Exit, fail5Out) = RunScript("scripts/qa/dated-obligations-check.ps1", "-AsOfDate 2026-11-01 -Registry testdata/qa/dated-obligations/fail-5.json -OutFile \"\"");
        var (warn20Exit, warn20Out) = RunScript("scripts/qa/dated-obligations-check.ps1", "-AsOfDate 2026-11-01 -Registry testdata/qa/dated-obligations/warn-20.json -OutFile \"\"");
        var (expiredExit, _) = RunScript("scripts/qa/dated-obligations-check.ps1", "-AsOfDate 2026-11-01 -Registry testdata/qa/dated-obligations/expired.json -OutFile \"\"");
        r.WriteScenarioSteps("""
            1. Real registry docs/qa/dated-obligations.json on today's date → exit 0, nearest open obligation printed.
            2. testdata/qa/dated-obligations/fail-5.json (due in 5 days) → exit 1 (::error::).
            3. warn-20.json (due in 20 days) → ::warning::, exit 0.
            4. expired.json (Declared, past due) → exit 1.
            Deviation from the plan text: the nearest obligation is the ObjectStorage CI exclusion (2026-12-14, PLAN-38), which the
            plan itself registers, not STR-002 (2027-01-31).
            """);
        r.WriteScenarioDbChecks(new
        {
            today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            real = new { exit = realExit, nearest },
            fail5 = new { exit = fail5Exit, error = fail5Out.Contains("::error::", StringComparison.Ordinal) },
            warn20 = new { exit = warn20Exit, warning = warn20Out.Contains("::warning::", StringComparison.Ordinal) },
            expired = new { exit = expiredExit }
        });
        Assert.True(realExit == 0, realOut);
        Assert.NotNull(nearest);
        Assert.Equal(1, fail5Exit);
        Assert.Equal(0, warn20Exit);
        Assert.Contains("::warning::", warn20Out, StringComparison.Ordinal);
        Assert.Equal(1, expiredExit);
        r.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 34c

    [SkippableFact]
    public void Ac34_c_governance_documents_start_today_and_the_pre_closure_matrix_fails_after_expiry()
    {
        var r = Start("34c", "AC-QA-34c: STR-005 matrix and decision log pass the startup rules today; the pre-closure matrix fails on 2026-09-16 with 'expired'");
        var matrixPath = Path.Combine(Root, "docs", "str-governance", "governance-matrix.json");
        var matrix = JsonSerializer.Deserialize<GovernanceMatrixDocument>(File.ReadAllText(matrixPath))!;
        var log = JsonSerializer.Deserialize<DecisionLogDocument>(File.ReadAllText(Path.Combine(Root, "docs", "str-governance", "decision-log.json")))!;
        var today = ProgramGovernanceEvaluator.Evaluate(matrix, log, goLiveDeclared: false, evidence: null, DateTime.UtcNow);

        var preClosure = JsonSerializer.Deserialize<GovernanceMatrixDocument>(File.ReadAllText(matrixPath))!;
        foreach (var ex in preClosure.Exceptions.Where(e => e.ExceptionId.StartsWith("EX-STR005-SIM-", StringComparison.Ordinal)))
            ex.Status = "Declared";
        var onExpiryDay = ProgramGovernanceEvaluator.Evaluate(preClosure, log, false, null, new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc));
        var dayAfter = ProgramGovernanceEvaluator.Evaluate(preClosure, log, false, null, new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));

        var closure = log.Decisions.SingleOrDefault(d => d.DecisionId == "PG-EXC-2026-10-001");
        r.WriteScenarioSteps("""
            1. ProgramGovernanceEvaluator.Evaluate (the rule ProgramGovernanceStartupValidator applies at startup with
               EnforceOnStartup=true) on the real matrix and decision log, now → no failure.
            2. The same matrix with both simulation exceptions back to Declared (the state before PLAN-34): 2026-09-15 → no
               failure; 2026-09-16 → "EX-STR005-SIM-INCIDENT-WINDOW: Declared exception expired".
            3. The closure decision PG-EXC-2026-10-001 is in the decision log. Unit canary: RuntimeGovernanceDocumentsTests
               starts the real validators with each service's Production/Staging settings on TimeProvider.System.
            """);
        r.WriteScenarioDbChecks(new
        {
            todayFailures = today,
            exceptions = matrix.Exceptions.Select(e => new { e.ExceptionId, e.Status, e.ExpiresOn }),
            preClosure = new { onExpiryDay, dayAfter },
            closureDecision = closure is null ? null : new { closure.DecisionId, closure.Type, closure.Status, closure.DecidedAt }
        });
        Assert.Empty(today);
        Assert.Empty(onExpiryDay);
        Assert.Contains(dayAfter, f => f.Contains("EX-STR005-SIM-INCIDENT-WINDOW", StringComparison.Ordinal) && f.Contains("expired", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(closure);
        Assert.DoesNotContain(matrix.Exceptions, e => e.Status == "Declared" && e.ExceptionId.StartsWith("EX-STR005-SIM-", StringComparison.Ordinal));
        r.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 34d, 34e, 34f, 34h (trx evidence of the Linux run)

    private static EvidenceRecorder StartTrx(string key, string scenario, out List<TrxResult> unit, out List<TrxResult> integration, out List<TrxResult> tc)
    {
        var r = Start(key, scenario);
        unit = LinuxTrx("unit-results.trx");
        integration = LinuxTrx("integration-results.trx");
        tc = TestcontainersTrx().Results;
        AcceptanceGate.RequireData(r, unit.Count > 0 && integration.Count > 0 && tc.Count > 0,
            "نتائج اختبارات تشغيل Linux غير موجودة", $"Linux trx files missing under {LinuxRun}/TestResults (run scripts/ci-local.ps1 -Out artifacts/ci-local/head).");
        return r;
    }

    [SkippableFact]
    public void Ac34_d_the_twelve_index_actions_are_branch_scoped()
    {
        var r = StartTrx("34d", "AC-QA-34d: twelve Index(employeeId) actions — BR1 rows only, BR2 employee 403, on SQLite and PostgreSQL; BolaArchitectureTests green", out var unit, out var integration, out var tc);
        var sqlite = Count(integration, "Plan034ScopedIndexTests.");
        var forbidden = Count(integration, "Plan034ScopedIndexTests.Branch_user_gets_403_for_other_branch_employee");
        var pg = Count(tc, "Plan034ScopedIndexPostgreSqlTests.");
        var bola = Count(unit, "BolaArchitectureTests.");
        r.WriteScenarioSteps("""
            Linux clean-HEAD run (scripts/ci-local.ps1), trx files:
            - Plan034ScopedIndexTests (SQLite, real controllers): 6 cases × 12 actions, including "BR2 employee → 403".
            - Plan034ScopedIndexPostgreSqlTests (Testcontainers): ContractsController and PaymentHoldsController on PostgreSQL.
            - BolaArchitectureTests: no Index(employeeId) action without a scope guard, no allow-list.
            """);
        r.WriteScenarioDbChecks(new { sqlite, forbidden, pg, pgSource = TestcontainersTrx().Source, bola });
        Assert.Equal((72, 72, 0), sqlite);
        Assert.Equal((12, 12, 0), forbidden);
        Assert.Equal((2, 2, 0), pg);
        Assert.True(bola.Total >= 1 && bola.Failed == 0 && bola.Passed == bola.Total, $"BolaArchitectureTests {bola}");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public void Ac34_e_separation_publish_with_the_regime_fake()
    {
        var r = StartTrx("34e", "AC-QA-34e: separation publish with the law50 fake; NoRegime → RegimeRulesNotFoundMessage, Unavailable → error, no event", out _, out var integration, out _);
        string[] planned =
        [
            "ContractLifecycleIntegrationTests.PublishSeparation_AlreadyPublished_ReturnsError",
            "ContractLifecycleIntegrationTests.PublishSeparation_Complete_PublishesOnce_IdempotentOnRetry",
            "ContractLifecycleIntegrationTests.PublishSeparation_IncompleteChecklist_ReturnsError_DoesNotAutoComplete",
            "ContractLifecycleIntegrationTests.UpdateAndDeleteSeparation_AfterPublish_AreBlocked",
            "Fun002SeparationRecordIntegrationTests.Publish_FutureEffectiveDate_Rejected",
            "Fun002SeparationRecordIntegrationTests.Publish_LegalHold_Rejected",
            "Int003ProcessSagaTests.DOM001_Publish_DoesNotAutoComplete_ContractTerminated",
            "EndToEndReleaseWorkflowSmokeIntegrationTests.ReleaseWorkflow_Contract_Leave_PayrollPost_Separation_ChainSucceeds"
        ];
        string[] regimeCases =
        [
            "Fun002SeparationRecordIntegrationTests.Publish_with_law50_regime_publishes_once_and_snapshots_the_regime",
            "Fun002SeparationRecordIntegrationTests.Publish_without_an_active_regime_is_refused_without_an_event",
            "Fun002SeparationRecordIntegrationTests.Publish_with_hr_core_unavailable_fails_without_an_event"
        ];
        var results = planned.Concat(regimeCases).ToDictionary(n => n, n => Count(integration, n));
        r.WriteScenarioSteps("""
            Linux clean-HEAD run, integration trx: the eight separation/contract tests of plan §5 (red at baseline:
            IHrCoreActiveRegimeClient not registered) and the three regime-client cases (law50 publishes once and snapshots
            law50; NoRegime → RegimeRulesNotFoundMessage and no event; Unavailable → HttpRequestException and no event).
            """);
        r.WriteScenarioDbChecks(results.ToDictionary(k => k.Key, k => new { k.Value.Total, k.Value.Passed, k.Value.Failed }));
        Assert.All(results, kv => Assert.True(kv.Value is { Total: 1, Passed: 1 }, $"{kv.Key}: {kv.Value}"));
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public void Ac34_f_payroll_calculates_on_sqlite_and_postgresql_and_host_validation_names_missing_clients()
    {
        var r = StartTrx("34f", "AC-QA-34f: payroll runs calculate on SQLite and PostgreSQL; a host without the regime client is named by IntegrationHostValidationTests", out _, out var integration, out var tc);
        var sqlite = new[] { "PayrollRunLifecycleIntegrationTests.", "Perf001PayrollBatchTests.", "Pay003ResumablePayrollRunTests." }
            .ToDictionary(n => n, n => Count(integration, n));
        var postgres = new[] { "Pay021DeterminismPostgreSqlTests.", "Pay022ResumePostgreSqlTests.", "Pay023FrozenRunIsolationTests.", "Perf001PayrollBatchPostgreSqlTests." }
            .ToDictionary(n => n, n => Count(tc, n));
        var hostValidation = Count(integration, "IntegrationHostValidationTests.");
        var negative = Count(integration, "IntegrationHostValidationTests.Payroll_host_without_the_service_regime_client_is_named");
        r.WriteScenarioSteps("""
            Linux clean-HEAD run: the payroll lifecycle/batch/resume tests on SQLite (9) and PostgreSQL (5) — red at baseline —
            pass (Calculated, ValidatedAtCalculate asserted by the tests); IntegrationHostValidationTests builds every shared
            host with ValidateOnBuild and names a missing IHrCoreServiceRegimeClient (negative case).
            """);
        r.WriteScenarioDbChecks(new { sqlite, postgres, postgresSource = TestcontainersTrx().Source, hostValidation, negative });
        Assert.All(sqlite.Concat(postgres), kv => Assert.True(kv.Value.Total > 0 && kv.Value.Failed == 0 && kv.Value.Passed == kv.Value.Total, $"{kv.Key}: {kv.Value}"));
        Assert.Equal(14, sqlite.Values.Sum(v => v.Passed) + postgres.Values.Sum(v => v.Passed));
        Assert.True(hostValidation.Failed == 0 && hostValidation.Passed == hostValidation.Total && hostValidation.Total >= 6, hostValidation.ToString());
        Assert.Equal((1, 1, 0), negative);
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public void Ac34_h_unit_suite_is_green_on_windows_ar_sa_and_on_linux()
    {
        var r = StartTrx("34h", "AC-QA-34h: full unit run Windows/ar-SA = Linux = 0 failures; TestCultureGuardTests guards the ar-SY culture", out var linuxUnit, out _, out _);
        var windowsUnit = LoadTrx(WindowsRun, "unit.trx");
        AcceptanceGate.RequireData(r, windowsUnit.Count > 0,
            "نتائج اختبارات الوحدة على Windows غير موجودة", $"Windows unit trx missing under {WindowsRun} (dotnet test src/Tests/HRManagement.UnitTests --logger \"trx;LogFileName=unit.trx\" --results-directory artifacts/plan34/windows).");
        var linux = (linuxUnit.Count, linuxUnit.Count(t => t.Outcome == "Passed"), linuxUnit.Count(t => t.Outcome == "Failed"));
        var windows = (windowsUnit.Count, windowsUnit.Count(t => t.Outcome == "Passed"), windowsUnit.Count(t => t.Outcome == "Failed"));
        var guardLinux = Count(linuxUnit, "TestCultureGuardTests.");
        var guardWindows = Count(windowsUnit, "TestCultureGuardTests.");
        var presence = Count(windowsUnit, "TestCultureInitializerPresenceTests.");
        r.WriteScenarioSteps("""
            1. Linux clean-HEAD unit trx and the Windows (machine culture ar-SA) unit trx of the same HEAD: 0 failures each,
               same test count.
            2. TestCultureGuardTests (thread and thread-pool culture = ar-SY, Gregorian) pass on both; removing TestCulture
               turns them red, and TestCultureInitializerPresenceTests checks the initializer in all three test projects.
            """);
        r.WriteScenarioDbChecks(new { linux, windows, guardLinux, guardWindows, presence });
        Assert.Equal(0, linux.Item3);
        Assert.Equal(0, windows.Item3);
        Assert.Equal(linux.Count, windows.Count);
        Assert.True(guardLinux.Total >= 2 && guardLinux.Failed == 0, guardLinux.ToString());
        Assert.True(guardWindows.Total >= 2 && guardWindows.Failed == 0, guardWindows.ToString());
        Assert.Equal(3, presence.Passed);
        r.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 34g

    [SkippableFact]
    public async Task Ac34_g_base_stack_runs_without_minio_and_scans_uploads()
    {
        var r = Start("34g", "AC-QA-34g: base compose stack (full-stack) without MinIO — file-storage on Local; clean upload → Clean, EICAR → quarantined; object-storage profile without MINIO_IMAGE fails at config");
        var gateway = Environment.GetEnvironmentVariable("ACQA34_BASE_STACK_URL");
        AcceptanceGate.RequireData(r, !string.IsNullOrWhiteSpace(gateway),
            "المكدّس الأساسي غير مشغّل", "Set ACQA34_BASE_STACK_URL (e.g. http://localhost:5000) after: bash scripts/prepare-ci-compose-env.sh; docker compose -f docker-compose.yml -f docker-compose.services.yml --profile full-stack up -d --wait");

        var (configExit, configOut) = Run("docker",
            "compose --env-file .env.ci -f docker-compose.yml -f docker-compose.services.yml --profile full-stack --profile object-storage config --quiet");
        var (overlayExit, overlayOut) = Run("docker",
            "compose --env-file .env.ci -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.object-storage.yml --profile full-stack config --quiet");
        var (minioPs, minioOut) = Run("docker", "ps --filter name=hrm-minio --format {{.Names}}");
        var (providerExit, provider) = Run("docker", "inspect hrm-file-storage-web --format {{range .Config.Env}}{{println .}}{{end}}");
        var providerLine = provider.Split('\n').FirstOrDefault(l => l.StartsWith("FileStorage__Provider=", StringComparison.Ordinal))?.Trim();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var health = await http.GetAsync(gateway!.TrimEnd('/') + "/health/live");

        var (scanExit, scanOut) = RunScript("scripts/acceptance-file-scan.ps1",
            $"-BaseUrl {gateway.TrimEnd('/')} -KeycloakBase {Environment.GetEnvironmentVariable("ACQA34_BASE_STACK_KEYCLOAK") ?? "http://localhost:8088"} " +
            $"-Username {Environment.GetEnvironmentVariable("ACQA34_BASE_STACK_USER") ?? "hradmin"} -Password {Environment.GetEnvironmentVariable("ACQA34_BASE_STACK_PASSWORD") ?? "HrAdmin123!"} " +
            $"-ClientSecret {Environment.GetEnvironmentVariable("KEYCLOAK_HR_GATEWAY_CLIENT_SECRET") ?? "ci-gateway-secret-lab-only"}");

        r.WriteScenarioSteps("""
            1. bash scripts/prepare-ci-compose-env.sh; docker compose -f docker-compose.yml -f docker-compose.services.yml
               --profile full-stack up -d --wait --wait-timeout 600 (no MINIO_IMAGE, no MinIO container).
            2. file-storage-web runs with FileStorage__Provider=Local; gateway /health/live = 200.
            3. scripts/acceptance-file-scan.ps1 through the gateway: a clean file → Clean and downloadable, EICAR →
               Infected/Quarantined and not downloadable.
            4. Negative: `--profile object-storage config` without MINIO_IMAGE fails; the object-storage overlay without it
               fails with the explicit message.
            """);
        r.WriteScenarioDbChecks(new
        {
            gateway,
            health = (int)health.StatusCode,
            minioContainers = minioOut.Trim(),
            fileStorageProvider = providerLine,
            providerInspectExit = providerExit,
            scan = new { exit = scanExit, output = scanOut.Length > 4000 ? scanOut[^4000..] : scanOut },
            objectStorageProfileWithoutImage = new { exit = configExit, output = configOut.Trim() },
            objectStorageOverlayWithoutImage = new { exit = overlayExit, output = overlayOut.Trim() }
        });
        Assert.Equal(200, (int)health.StatusCode);
        Assert.Equal(0, minioPs);
        Assert.True(string.IsNullOrWhiteSpace(minioOut), "a MinIO container is running");
        Assert.Equal("FileStorage__Provider=Local", providerLine);
        Assert.True(scanExit == 0, scanOut);
        Assert.NotEqual(0, configExit);
        Assert.NotEqual(0, overlayExit);
        Assert.Contains("MINIO_IMAGE", overlayOut, StringComparison.Ordinal);
        r.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 34i

    [SkippableFact]
    public void Ac34_i_no_high_or_critical_vulnerability_and_warnings_are_errors()
    {
        var r = Start("34i", "AC-QA-34i: no High/Critical package vulnerability, the CI build runs -warnaserror; the vulnerable SSH.NET fails restore with NU1903");
        var (vulnExit, vulnOut) = Run("dotnet", "list HRManagement.slnx package --vulnerable --include-transitive", 900_000);
        var vulnerable = vulnOut.Split('\n').Where(l => l.Contains("has the following vulnerable packages", StringComparison.Ordinal)).Select(l => l.Trim()).ToList();
        var highOrCritical = vulnOut.Split('\n').Count(l => l.Contains(" High ", StringComparison.Ordinal) || l.Contains(" Critical ", StringComparison.Ordinal));

        var summary = LoadSummary();
        var buildStep = (summary?["steps"] as JsonArray ?? []).FirstOrDefault(s => (string?)s!["name"] == "Build");
        using var steps = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "scripts", "ci", "steps.json")));
        var buildRun = steps.RootElement.GetProperty("steps").EnumerateArray().First(s => s.GetProperty("name").GetString() == "Build").GetProperty("run").GetString();
        var buildProps = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));

        // Negative probe: a project inside the repository (so Directory.Build.props applies) that references SSH.NET 2025.1.0.
        var probe = Path.Combine(Root, "artifacts", "acqa34-ssh-probe");
        Directory.CreateDirectory(probe);
        File.WriteAllText(Path.Combine(probe, "Probe.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="SSH.NET" Version="2025.1.0" />
              </ItemGroup>
            </Project>
            """);
        var (probeExit, probeOut) = Run("dotnet", $"restore \"{Path.Combine(probe, "Probe.csproj")}\"", 600_000);

        r.WriteScenarioSteps("""
            1. dotnet list HRManagement.slnx package --vulnerable --include-transitive → no project with vulnerable packages.
            2. scripts/ci/steps.json "Build" runs with -warnaserror and passed in the clean-HEAD Linux run;
               Directory.Build.props makes NU1903/NU1904 errors for every build.
            3. Negative: a probe project under artifacts/ referencing SSH.NET 2025.1.0 fails restore with NU1903.
            """);
        r.WriteScenarioDbChecks(new
        {
            vulnExit,
            vulnerableProjects = vulnerable,
            highOrCritical,
            buildRun,
            buildStepStatus = (string?)buildStep?["status"],
            warningsAsErrors = buildProps.Contains("NU1903;NU1904", StringComparison.Ordinal),
            probe = new { exit = probeExit, nu1903 = probeOut.Contains("NU1903", StringComparison.Ordinal) }
        });
        Assert.Equal(0, vulnExit);
        Assert.Empty(vulnerable);
        Assert.Equal(0, highOrCritical);
        Assert.Contains("-warnaserror", buildRun, StringComparison.Ordinal);
        Assert.Equal("passed", (string?)buildStep?["status"]);
        Assert.Contains("NU1903;NU1904", buildProps, StringComparison.Ordinal);
        Assert.NotEqual(0, probeExit);
        Assert.Contains("NU1903", probeOut, StringComparison.Ordinal);
        r.WriteResult("Pass");
    }
}
