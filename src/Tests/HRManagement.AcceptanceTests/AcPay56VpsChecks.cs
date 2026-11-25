using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using Microsoft.Playwright;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-56 (P9, P-15, P-16) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers, as three
/// people: <c>e2e-payroll-processor</c> creates, calculates and cancels, <c>e2e-payroll-approver</c> rejects and decides, and the
/// administrator (<c>hradmin</c>, or the user named in <c>PAY056_VPS_ADMIN</c>), who holds the reversal policy, asks for a reversal.
/// The password is read into memory only; evidence goes under <c>artifacts/plan56/vps</c> and is never committed.
/// <para>
/// <b>These checks never cancel, reject, reverse or recalculate a run of the demo, and never send the approval of a reversal.</b>
/// The runs they act on are their own: made in a month no demo run covers (from September 2028 on, found by the month alone: the
/// demo's own runs are of 2026), each ended <c>Cancelled</c> whatever fails on the way. On a demo posted run they only ask for a
/// reversal and have the request rejected with a note; the one act of the demo's approved run is a validation that must be refused.
/// The demo's runs, lines and batches are compared before and after by checksums, and with P0's picture when
/// <c>PAY056_VPS_LINES_MD5</c> carries its value. No amount is written into evidence.
/// </para>
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Pay56Processor = "e2e-payroll-processor";
    private const string Pay56Approver = "e2e-payroll-approver";
    private const string Pay56Api = "/ui/payroll/api/payroll";
    private const string Pay56Runs = "/ui/payroll/PayrollRuns";

    /// <summary>The first month of the checks' own runs; every demo run is earlier. What marks a run as the checks'.</summary>
    private const string Pay56FirstMonth = "2028-09-01";
    private const string Pay56Mine = "\"PeriodStart\" >= '" + Pay56FirstMonth + "'";
    private const string Pay56Demo = "\"PeriodStart\" < '" + Pay56FirstMonth + "'";

    private const string Pay56RejectionNote = "بدل النقل لم يُحتسب لعدد من العاملين في مديرية الشؤون الإدارية؛ يُصحَّح ثم يُعاد الإرسال.";
    private const string Pay56CancellationReason = "أُنشئ التشغيل قبل اكتمال اعتماد تسويات الشهر؛ يُنشأ من جديد بعد اعتمادها.";
    private const string Pay56ReversalReason = "طلب مراجعة: يُشتبه أن التشغيل رُحّل قبل اعتماد تسوية أحد العاملين.";
    private const string Pay56ReversalRejection = "دُفعت رواتب هذا التشغيل؛ لا يُعكس، وما صُرف زيادةً يُصحَّح بتشغيل تصحيح.";
    private const string Pay56ValidationRefusal = "لا يمكن التحقق من تشغيل";
    private const string Pay56OwnDecisionRefusal = "من طلبه";

    /// <summary>
    /// The checks' own reversal requests, told from anyone else's by their reason (compared by its MD5, so that the statement sent
    /// to the server is ASCII). A request a person of the demo made is never touched.
    /// </summary>
    private static readonly string Pay56MyRequests =
        "md5(\"Reason\") = '" + Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(Pay56ReversalReason))).ToLowerInvariant() + "'";

    private static string Pay56Admin => Environment.GetEnvironmentVariable("PAY056_VPS_ADMIN") is { Length: > 0 } user ? user : "hradmin";

    private static EvidenceRecorder StartPay56(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"pay56-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan56", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    private static string Pay56One(string sql) => Sql("payroll_db", sql).SingleOrDefault() ?? "";

    /// <summary>
    /// What no check may move: the demo's runs (state, version, flags, total, poster, canceller), their batches, their decisions and
    /// their lines, each as a count and a checksum. The lines' checksum is P0's expression (id, gross, deductions, net).
    /// </summary>
    private static string Pay56DemoData() =>
        Pay56One(
            "select (select count(*) || ':' || md5(coalesce(string_agg(r.\"Id\"::text || r.\"Status\"::text || r.\"Revision\"::text || r.\"ResultsFrozen\"::text || " +
            "r.\"ValidatedAtCalculate\"::text || r.\"TotalNetPay\"::text || coalesce(r.\"PostedBy\"::text,'') || coalesce(r.\"CancelledBy\"::text,''), ';' order by r.\"Id\"), '')) " +
            $"from \"PayrollRuns\" r where r.{Pay56Demo}) || '~' || " +
            "(select count(*) || ':' || md5(coalesce(string_agg(b.\"Id\"::text || b.\"Status\" || b.\"BatchKind\" || b.\"TotalAmount\"::text, ';' order by b.\"Id\"), '')) " +
            $"from \"PaymentBatches\" b join \"PayrollRuns\" r on r.\"Id\" = b.\"PayrollRunId\" where r.{Pay56Demo}) || '~' || " +
            "(select count(*) || ':' || md5(coalesce(string_agg(a.\"Id\"::text || a.\"Decision\", ';' order by a.\"Id\"), '')) " +
            $"from \"PayrollRunApprovals\" a join \"PayrollRuns\" r on r.\"Id\" = a.\"PayrollRunId\" where r.{Pay56Demo}) || '~' || " + Pay56DemoLinesSql());

    private static string Pay56DemoLinesSql() =>
        "(select count(*) || ':' || md5(coalesce(string_agg(l.\"Id\"::text||'|'||l.\"GrossPay\"::text||'|'||l.\"TotalDeductions\"::text||'|'||l.\"NetPay\"::text, ';' order by l.\"Id\"), '')) " +
        $"from \"SalaryLines\" l join \"PayrollRuns\" r on r.\"Id\" = l.\"PayrollRunId\" where r.{Pay56Demo})";

    /// <summary>The free inputs: deductions, adjustments and absences no run has applied.</summary>
    private static string Pay56FreeInputs() =>
        Pay56One(
            "select (select count(*) from \"DeductionRecords\" where \"AppliedInRunId\" is null) || '|' || " +
            "(select count(*) from \"PayrollAdjustments\" where \"PayrollRunId\" is null) || '|' || " +
            "(select count(*) from \"AbsenceDeductions\" where \"AppliedInRunId\" is null)");

    /// <summary>A run without its amounts: state, version, frozen, validated, lines, has a canceller, the cancellation's reason.</summary>
    private static string Pay56Run(string id) =>
        Pay56One(
            "select r.\"Status\" || '~' || r.\"Revision\" || '~' || r.\"ResultsFrozen\"::text || '~' || r.\"ValidatedAtCalculate\"::text || '~' || " +
            "(select count(*) from \"SalaryLines\" l where l.\"PayrollRunId\" = r.\"Id\") || '~' || (r.\"CancelledBy\" is not null)::text || '~' || coalesce(r.\"CancellationReason\", '') " +
            $"from \"PayrollRuns\" r where r.\"Id\" = '{id}'");

    /// <summary>The first month from September 2028 that no live run covers (a cancelled run of an earlier check leaves its month free).</summary>
    private static DateOnly Pay56FreeMonth()
    {
        var month = Pay56One(
            $"select to_char(m, 'YYYY-MM-DD') from generate_series('{Pay56FirstMonth}'::date, '2032-08-01'::date, interval '1 month') m " +
            "where not exists (select 1 from \"PayrollRuns\" r where r.\"Status\" not in (5, 8) " +
            "and r.\"PeriodStart\"::date <= (m + interval '1 month' - interval '1 day')::date and r.\"PeriodEnd\"::date >= m::date) order by m limit 1");
        return DateOnly.ParseExact(month, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>A signed-in session that sends the antiforgery token of the run's API (the cookie stays in the client).</summary>
    private static async Task<HttpClient> Pay56ApiAsync(string user, string password)
    {
        var (client, _) = await SignInAsync(user, password, "/ui/payroll/");
        using var issued = await client.GetAsync($"https://{AppDomain}{Pay56Api}/run-lifecycle/antiforgery");
        var body = await issued.Content.ReadAsStringAsync();
        Assert.True(issued.IsSuccessStatusCode, $"The antiforgery token of the run's API answered {(int)issued.StatusCode} to {user}.");
        using var token = JsonDocument.Parse(body);
        client.DefaultRequestHeaders.Add(token.RootElement.GetProperty("headerName").GetString()!, token.RootElement.GetProperty("requestToken").GetString());
        return client;
    }

    private static Task<(int Status, string Error, string Body)> Pay56PostAsync(HttpClient client, string path, object? body = null) =>
        Pay55PostAsync(client, $"{Pay56Api}/{path}", body ?? new { });

    private static string Pay56IdOf(string body) => Regex.Match(body, "\"id\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"").Groups[1].Value;

    private static async Task<string> Pay56CreateRunAsync(HttpClient processor, DateOnly month)
    {
        var end = month.AddMonths(1).AddDays(-1);
        var (created, error, body) = await Pay56PostAsync(processor, "runs", new
        {
            periodStart = month.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            periodEnd = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            runDate = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        });
        var id = Pay56IdOf(body);
        Assert.True(created is 200 or 201 && id.Length == 36, $"Creating the run of {month:yyyy-MM} answered {created}: {error}");
        return id;
    }

    /// <summary>
    /// Leaves nothing open: every run of the checks that is not ended is cancelled (by the processor, or by the approver once it was
    /// sent for review), and every reversal request that still waits is rejected with its note. Never a demo run.
    /// </summary>
    private static async Task Pay56CloseAsync(HttpClient processor, HttpClient approver)
    {
        foreach (var run in Sql("payroll_db", $"select \"Id\" from \"PayrollRuns\" where {Pay56Mine} and \"Status\" not in (2, 5, 8)"))
        {
            var (cancelled, _, _) = await Pay56PostAsync(processor, $"runs/{run}/cancel", new { reason = Pay56CancellationReason });
            if (cancelled != 200)
                await Pay56PostAsync(approver, $"runs/{run}/cancel", new { reason = Pay56CancellationReason });
        }

        foreach (var request in Sql("payroll_db", $"select \"Id\" from \"PayrollReversalRequests\" where \"Status\" = 'Pending' and {Pay56MyRequests}"))
            await Pay56PostAsync(approver, $"reversal-requests/{request}/decide", new { approve = false, notes = Pay56ReversalRejection });
    }

    private static string Pay56OpenLeft() =>
        Pay56One(
            $"select (select count(*) from \"PayrollRuns\" where {Pay56Mine} and \"Status\" not in (5, 8)) || '|' || " +
            $"(select count(*) from \"PayrollReversalRequests\" where \"Status\" = 'Pending' and {Pay56MyRequests})");

    /// <summary>A demo posted run on which no request waits (the oldest), or null.</summary>
    private static string? Pay56PostedRun() =>
        Sql("payroll_db",
            $"select r.\"Id\" from \"PayrollRuns\" r where r.\"Status\" = 2 and r.{Pay56Demo} and not exists " +
            "(select 1 from \"PayrollReversalRequests\" q where q.\"PayrollRunId\" = r.\"Id\" and q.\"Status\" = 'Pending') order by r.\"PeriodStart\" limit 1").SingleOrDefault();

    // ---------------------------------------------------------------- 1. the migrations

    [SkippableFact]
    public void AC_PAY_56_vps_the_migrations_ran_every_line_has_its_adjustments_and_no_amount_changed()
    {
        var recorder = StartPay56("migration",
            "Demo server: the two PAY056 migrations are recorded; every line's TotalAdjustments is what its adjustment components add up to; the lines' checksum (id, gross, deductions, net) is P0's; no line's stored net differs from the formula; the new tables are under forced row security and the lines kept theirs; the run indexes exist; every demo run is at revision 1.");
        var migrations = Pay56One("select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" like '%PAY056_NetFormula' or \"MigrationId\" like '%PAY056_RunLifecycle'");
        var rowSecurity = Pay56One(
            "select string_agg(relname || ':' || relrowsecurity::text || ':' || relforcerowsecurity::text, ',' order by relname) from pg_class " +
            "where relname in ('PayrollRunRevisions', 'PayrollReversalRequests', 'SalaryLines', 'SalaryLineComponents') and relkind = 'r'");
        var indexes = Pay56One(
            "select count(*) from pg_indexes where indexdef like 'CREATE UNIQUE INDEX%' and indexname in " +
            "('IX_PayrollRuns_LivePeriod', 'IX_PayrollRuns_OneOpenCorrectionPerSource', 'IX_PayrollReversalRequests_OnePending', 'IX_PayrollRunRevisions_Run_Revision')");
        var liveIndex = Pay56One("select indexdef from pg_indexes where indexname = 'IX_PayrollRuns_LivePeriod'");
        var columns = Pay56One(
            "select (select count(*) from information_schema.columns where table_name = 'PayrollRuns' and column_name in " +
            "('Revision', 'PostedBy', 'PostedAt', 'CancelledBy', 'CancelledAt', 'CancellationReason')) || '|' || " +
            "(select count(*) from information_schema.columns where (table_name = 'SalaryLines' and column_name = 'TotalAdjustments') " +
            "or (table_name = 'PayrollAdjustments' and column_name = 'TargetSourceRunId'))");
        // The fill: a line's adjustments are what its referenced adjustment components add up to (none on the demo: all zero).
        var fill = Pay56One(
            "with adj as (select c.\"SalaryLineId\" id, sum(c.\"Amount\") a from \"SalaryLineComponents\" c where c.\"ComponentType\" = 'Adjustment' and c.\"ReferenceId\" is not null group by 1) " +
            "select count(*) || '|' || count(*) filter (where l.\"TotalAdjustments\" <> coalesce(adj.a, 0)) || '|' || count(*) filter (where l.\"TotalAdjustments\" <> 0) " +
            $"from \"SalaryLines\" l join \"PayrollRuns\" r on r.\"Id\" = l.\"PayrollRunId\" left join adj on adj.id = l.\"Id\" where r.{Pay56Demo}");
        // The historical lines: a stored net that differs from the formula (a posting of an older release that rewrote a net).
        var mismatches = Pay56One(
            "select coalesce(string_agg(s || ':' || n, ',' order by s), '') from (select r.\"Status\" s, " +
            "count(*) filter (where abs(l.\"NetPay\" - greatest(0, l.\"GrossPay\" - l.\"TotalDeductions\" + l.\"TotalAdjustments\")) > 0.01) n " +
            $"from \"SalaryLines\" l join \"PayrollRuns\" r on r.\"Id\" = l.\"PayrollRunId\" where r.{Pay56Demo} group by 1) t");
        var lines = Pay56One("select " + Pay56DemoLinesSql());
        var runs = Pay56One(
            "select string_agg(to_char(\"PeriodStart\", 'YYYY-MM') || ':' || \"Status\" || ':' || \"Revision\" || ':' || (\"PostedBy\" is null)::text || ':' || (\"CancelledBy\" is null)::text, ',' order by \"PeriodStart\") " +
            $"from \"PayrollRuns\" where {Pay56Demo}");
        var pictureLines = Environment.GetEnvironmentVariable("PAY056_VPS_LINES_MD5");
        var pictureRuns = Environment.GetEnvironmentVariable("PAY056_VPS_RUNS");

        recorder.WriteScenarioDbChecks(new
        {
            migrations,
            rowSecurity,
            indexes,
            liveIndex,
            columns,
            columnsFormat = "new columns of the run | TotalAdjustments and TargetSourceRunId",
            fill,
            fillFormat = "demo lines | lines whose TotalAdjustments differs from their adjustment components | lines with adjustments",
            historicalNetMismatches = mismatches,
            mismatchesFormat = "run status:lines whose stored net differs from max(0, gross - deductions + adjustments)",
            lines,
            linesFormat = "count:checksum of (id, gross, deductions, net), P0's expression",
            runs,
            runsFormat = "month:status:revision:no poster recorded:not cancelled",
            comparedWithThePicture = pictureLines is not null
        });

        Assert.Equal("2", migrations);
        Assert.Equal("PayrollReversalRequests:true:true,PayrollRunRevisions:true:true,SalaryLineComponents:true:true,SalaryLines:true:true", rowSecurity);
        Assert.Equal("4", indexes);
        Assert.Contains("'Regular'", liveIndex, StringComparison.Ordinal);
        Assert.Equal("6|2", columns);
        var filled = fill.Split('|');
        Assert.Equal("0", filled[1]);                                     // every line's adjustments are its components'
        Assert.DoesNotContain(mismatches.Split(',', StringSplitOptions.RemoveEmptyEntries), m => !m.EndsWith(":0", StringComparison.Ordinal));
        Assert.StartsWith(filled[0] + ":", lines, StringComparison.Ordinal);
        // No demo run moved: each at revision 1, none cancelled by a check.
        Assert.All(runs.Split(','), r => Assert.EndsWith(":1:true:true", r, StringComparison.Ordinal));
        if (pictureLines is not null) Assert.Equal(pictureLines, lines.Split(':')[1]);
        if (pictureRuns is not null) Assert.Equal(pictureRuns, string.Join(",", runs.Split(',').Select(r => string.Join(":", r.Split(':').Take(2)))));
        recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 2–3. a rejection that reopens; a cancellation

    [SkippableFact]
    public async Task AC_PAY_56_vps_a_run_of_the_checks_is_rejected_and_reopened_then_cancelled_and_its_inputs_are_free_again()
    {
        var recorder = StartPay56("lifecycle",
            "Demo server: the processor creates a run for a month no demo run covers and calculates it. If the demo calculates: the run validates, is sent for review, a rejection without a note answers 400, the approver's rejection with a note reopens it as a draft at revision 2 with its revision recorded, the processor calculates it again and cancels it with a reason. If the demo refuses to calculate (its pay profiles carry no legal regime: PLAN-55), the refusal and its message are recorded and the cancellation is proved on the draft. Either way the run ends cancelled with who, when and why, the free inputs counted before are free again, and no demo run, line or batch changed.");
        var password = DemoPassword();
        using var processor = await Pay56ApiAsync(Pay56Processor, password);
        using var approver = await Pay56ApiAsync(Pay56Approver, password);
        var demoBefore = Pay56DemoData();
        var inputsBefore = Pay56FreeInputs();
        var month = Pay56FreeMonth();
        try
        {
            var run = await Pay56CreateRunAsync(processor, month);
            var created = Pay56Run(run);
            var (calculated, calculationError, _) = await Pay56PostAsync(processor, $"runs/{run}/calculate");
            var afterCalculation = Pay56Run(run);
            var inputsInTheRun = Pay56FreeInputs();

            int noNote = 0, rejected = 0, recalculated = 0, submitted = 0;
            string rejectionError = "", reopened = "", revision = "", afterRecalculation = "", recalculationError = "";
            if (calculated == 200)
            {
                (submitted, _, _) = await Pay56PostAsync(processor, $"runs/{run}/submit-for-review");
                (noNote, _, _) = await Pay56PostAsync(approver, $"runs/{run}/reject", new { notes = " " });
                var underReview = Pay56Run(run);
                Assert.StartsWith("3~1~true~true~", underReview, StringComparison.Ordinal);
                (rejected, rejectionError, _) = await Pay56PostAsync(approver, $"runs/{run}/reject", new { notes = Pay56RejectionNote });
                reopened = Pay56Run(run);
                revision = Pay56One(
                    "select \"Revision\" || '~' || \"LineCount\" || '~' || length(\"LinesFingerprint\") || '~' || \"Note\" " +
                    $"from \"PayrollRunRevisions\" where \"PayrollRunId\" = '{run}'");
                (recalculated, recalculationError, _) = await Pay56PostAsync(processor, $"runs/{run}/calculate");
                afterRecalculation = Pay56Run(run);
            }

            // The cancellation: without a reason first, then with one.
            var beforeCancellation = Pay56Run(run);
            var (noReason, _, _) = await Pay56PostAsync(processor, $"runs/{run}/cancel", new { reason = " " });
            var afterNoReason = Pay56Run(run);
            var (cancelled, cancellationError, _) = await Pay56PostAsync(processor, $"runs/{run}/cancel", new { reason = Pay56CancellationReason });
            var ended = Pay56Run(run);
            var decision = Pay56One($"select \"Decision\" || '~' || coalesce(\"Notes\", '') from \"PayrollRunApprovals\" where \"PayrollRunId\" = '{run}' order by \"DecidedAt\" desc limit 1");
            var auditRows = Pay56One($"select count(*) from \"AuditLogs\" where \"EntityId\" = '{run}'");
            var inputsAfter = Pay56FreeInputs();
            var demoAfter = Pay56DemoData();
            // The period is free again: a cancelled run does not hold its month.
            var monthFreeAgain = Pay56FreeMonth() == month;

            recorder.WriteScenarioDbChecks(new
            {
                run,
                month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                created,
                runFormat = "status~revision~frozen~validated~lines~has a canceller~cancellation reason",
                calculation = calculated,
                theDemoRefusesToCalculate = calculated != 200,
                calculationRefusal = calculated == 200 ? "" : calculationError,
                afterCalculation,
                inputsBefore,
                inputsInTheRun,
                inputsFormat = "free deductions | free adjustments | free absences",
                submitted,
                rejectionWithoutANote = noNote,
                rejection = rejected,
                rejectionError,
                reopened,
                revision,
                revisionFormat = "revision~lines~fingerprint length~note",
                recalculation = recalculated,
                recalculationError,
                afterRecalculation,
                cancellationWithoutAReason = noReason,
                cancellation = cancelled,
                cancellationError,
                ended,
                decision,
                auditRows,
                inputsAfter,
                monthFreeAgain,
                demoUnchanged = demoBefore == demoAfter
            });

            Assert.Equal("0~1~false~false~0~false~", created);
            if (calculated == 200)
            {
                Assert.StartsWith("1~1~true~true~", afterCalculation, StringComparison.Ordinal);
                Assert.Equal(200, submitted);
                Assert.Equal(400, noNote);
                Assert.True(rejected == 200, $"The rejection answered {rejected}: {rejectionError}");
                Assert.Equal("0~2~false~false~0~false~", reopened);
                Assert.StartsWith("1~", revision, StringComparison.Ordinal);
                Assert.EndsWith($"~64~{Pay56RejectionNote}", revision, StringComparison.Ordinal);
                Assert.True(recalculated == 200, $"The calculation of the reopened run answered {recalculated}: {recalculationError}");
                Assert.StartsWith("1~2~true~true~", afterRecalculation, StringComparison.Ordinal);
            }
            else
            {
                // The refusal is the demo's (PLAN-55 §13: its pay profiles carry no legal regime): 400 with a message, the run a draft.
                Assert.Equal(400, calculated);
                Assert.False(string.IsNullOrWhiteSpace(calculationError), "The demo refused to calculate without saying why.");
                Assert.StartsWith("0~1~", afterCalculation, StringComparison.Ordinal);
                Assert.Equal(inputsBefore, inputsInTheRun);
            }

            Assert.Equal(400, noReason);
            Assert.Equal(beforeCancellation, afterNoReason);
            Assert.True(cancelled == 200, $"The cancellation answered {cancelled}: {cancellationError}");
            Assert.StartsWith("8~", ended, StringComparison.Ordinal);
            Assert.EndsWith($"~true~{Pay56CancellationReason}", ended, StringComparison.Ordinal);
            Assert.Equal($"Cancelled~{Pay56CancellationReason}", decision);
            Assert.True(int.Parse(auditRows, CultureInfo.InvariantCulture) >= 2, $"The run's acts are not in the audit log: {auditRows}");   // created, cancelled
            Assert.Equal(inputsBefore, inputsAfter);
            Assert.True(monthFreeAgain, "The cancelled run still holds its month.");
            Assert.Equal(demoBefore, demoAfter);
        }
        finally
        {
            await Pay56CloseAsync(processor, approver);
        }

        Assert.Equal("0|0", Pay56OpenLeft());
        recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 4. the guards

    [SkippableFact]
    public async Task AC_PAY_56_vps_a_validation_does_not_bring_the_demos_approved_run_back()
    {
        var recorder = StartPay56("guard",
            "Demo server: validate on the demo's approved run answers 400 with the message that a validation revives nothing; the run's state, version, flags, total and lines are as they were, and so is every other run, line and batch of the demo.");
        var approvedRun = Sql("payroll_db", $"select \"Id\" from \"PayrollRuns\" where \"Status\" = 4 and {Pay56Demo} order by \"PeriodStart\" limit 1").SingleOrDefault();
        AcceptanceGate.RequireData(recorder, approvedRun is not null, "لا يوجد تشغيل معتمد على خادم العرض", "The demo has no approved run.");
        var password = DemoPassword();
        using var processor = await Pay56ApiAsync(Pay56Processor, password);
        var demoBefore = Pay56DemoData();
        var before = Pay56Run(approvedRun!);

        var (validated, error, _) = await Pay56PostAsync(processor, $"runs/{approvedRun}/validate");

        var after = Pay56Run(approvedRun!);
        var demoAfter = Pay56DemoData();
        recorder.WriteScenarioDbChecks(new { run = approvedRun, validation = validated, error, before, after, demoUnchanged = demoBefore == demoAfter });

        Assert.Equal(400, validated);
        Assert.Contains(Pay56ValidationRefusal, error, StringComparison.Ordinal);
        Assert.StartsWith("4~1~", before, StringComparison.Ordinal);
        Assert.Equal(before, after);
        Assert.Equal(demoBefore, demoAfter);
        recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 5. a reversal request, refused

    [SkippableFact]
    public async Task AC_PAY_56_vps_a_reversal_request_on_a_posted_run_is_not_decided_by_its_requester_and_the_approver_rejects_it()
    {
        var recorder = StartPay56("reversal-request",
            "Demo server: the administrator asks for the reversal of a demo posted run (400 without a reason; 409 for a second request while one waits). His own decision is refused; the processor, who does not hold the approval policy, is refused; the approver rejects the request with a note (400 without one). The request ends rejected, decided by another person than its requester; the run, its batch and every line are unchanged. No approval of a reversal is ever sent on the demo.");
        var password = DemoPassword();
        using var admin = await Pay56ApiAsync(Pay56Admin, password);
        using var processor = await Pay56ApiAsync(Pay56Processor, password);
        using var approver = await Pay56ApiAsync(Pay56Approver, password);
        // A request an interrupted earlier check left waiting is rejected first: one request waits on a run at a time.
        await Pay56CloseAsync(processor, approver);
        var postedRun = Pay56PostedRun();
        AcceptanceGate.RequireData(recorder, postedRun is not null, "لا يوجد تشغيل مرحّل بلا طلب عكس معلّق على خادم العرض", "The demo has no posted run without a waiting reversal request.");
        var demoBefore = Pay56DemoData();
        var requestsBefore = Pay56One("select count(*) from \"PayrollReversalRequests\"");
        try
        {
            var requests = $"runs/{postedRun}/reversal-requests";
            var (noReason, _, _) = await Pay56PostAsync(admin, requests, new { reason = " " });
            var afterNoReason = Pay56One("select count(*) from \"PayrollReversalRequests\"");
            var (requested, requestError, requestBody) = await Pay56PostAsync(admin, requests, new { reason = Pay56ReversalReason });
            var request = Pay56IdOf(requestBody);
            Assert.True(requested == 200 && request.Length == 36,
                $"The request by {Pay56Admin} answered {requested}: {requestError}. If it is 403 the user does not hold Payroll.Reverse on this server: name another in PAY056_VPS_ADMIN.");
            var (second, _, _) = await Pay56PostAsync(admin, requests, new { reason = Pay56ReversalReason });

            // The requester's own decision. It is sent as a rejection: the rule is the same for both, and no approval is sent here.
            var decide = $"reversal-requests/{request}/decide";
            var (own, ownError, _) = await Pay56PostAsync(admin, decide, new { approve = false, notes = Pay56ReversalRejection });
            var afterOwn = Pay56One($"select \"Status\" from \"PayrollReversalRequests\" where \"Id\" = '{request}'");
            var (byTheProcessor, _, _) = await Pay56PostAsync(processor, decide, new { approve = false, notes = Pay56ReversalRejection });
            var (noNote, _, _) = await Pay56PostAsync(approver, decide, new { approve = false, notes = " " });
            var afterNoNote = Pay56One($"select \"Status\" from \"PayrollReversalRequests\" where \"Id\" = '{request}'");
            var (rejected, rejectionError, _) = await Pay56PostAsync(approver, decide, new { approve = false, notes = Pay56ReversalRejection });
            var ended = Pay56One(
                "select \"Status\" || '~' || (\"DecidedBy\" is not null and \"DecidedBy\" <> \"RequestedBy\")::text || '~' || coalesce(\"DecisionNotes\", '') || '~' || \"Reason\" " +
                $"from \"PayrollReversalRequests\" where \"Id\" = '{request}'");
            var (again, _, _) = await Pay56PostAsync(approver, decide, new { approve = false, notes = Pay56ReversalRejection });
            using var listed = await approver.GetAsync($"https://{AppDomain}{Pay56Api}/reversal-requests?status=Rejected");
            var list = await listed.Content.ReadAsStringAsync();
            var demoAfter = Pay56DemoData();

            recorder.WriteScenarioDbChecks(new
            {
                run = postedRun,
                admin = Pay56Admin,
                requestWithoutAReason = noReason,
                request,
                requested,
                secondRequestWhileOneWaits = second,
                theRequestersOwnDecision = own,
                ownError,
                afterOwn,
                aDecisionByTheProcessor = byTheProcessor,
                rejectionWithoutANote = noNote,
                rejection = rejected,
                rejectionError,
                ended,
                requestFormat = "status~decided by another person~note~reason",
                aSecondDecision = again,
                listedAmongTheRejected = list.Contains(request, StringComparison.OrdinalIgnoreCase),
                demoUnchanged = demoBefore == demoAfter
            });

            Assert.Equal(400, noReason);
            Assert.Equal(requestsBefore, afterNoReason);
            Assert.Equal(409, second);
            // The requester never decides his own request: the handler's rule (400) where he holds the approval policy, the policy
            // (403) where he does not. Either way nothing changes.
            Assert.True(own is 400 or 403, $"The requester's own decision answered {own}: {ownError}");
            if (own == 400) Assert.Contains(Pay56OwnDecisionRefusal, ownError, StringComparison.Ordinal);
            Assert.Equal("Pending", afterOwn);
            Assert.Equal(403, byTheProcessor);
            Assert.Equal(400, noNote);
            Assert.Equal("Pending", afterNoNote);
            Assert.True(rejected == 200, $"The rejection answered {rejected}: {rejectionError}");
            Assert.Equal($"Rejected~true~{Pay56ReversalRejection}~{Pay56ReversalReason}", ended);
            Assert.Equal(409, again);
            Assert.Contains(request, list, StringComparison.OrdinalIgnoreCase);
            // The run is posted, its batch as it was, every line as it was.
            Assert.Equal(demoBefore, demoAfter);
        }
        finally
        {
            await Pay56CloseAsync(processor, approver);
        }

        Assert.Equal("0|0", Pay56OpenLeft());
        recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 6. the screens

    [SkippableFact]
    public async Task AC_PAY_56_vps_the_runs_screens_open_in_arabic_under_the_enforced_policy_with_names_and_the_payslip_shows_the_adjustments()
    {
        var recorder = StartPay56("screens",
            "Demo server: the run's page with its new acts (a draft run of the checks: the cancellation's form; a demo posted run: its reversal request), the reversal requests' list where the approver decides, the adjustment's form with the posted run it can be aimed at, the adjustments' list and the payslips' page open in Chromium at 1366×768 and 390×844 with one enforced Content-Security-Policy and no violation; the pages are Arabic and right-to-left, a person is named through the registry and no identifier is printed; the payslip of a demo line answers gross, deductions, adjustments and net, the net being the formula's.");
        var password = DemoPassword();
        using var admin = await Pay56ApiAsync(Pay56Admin, password);
        using var processor = await Pay56ApiAsync(Pay56Processor, password);
        using var approver = await Pay56ApiAsync(Pay56Approver, password);
        await Pay56CloseAsync(processor, approver);
        var postedRun = Pay56PostedRun();
        AcceptanceGate.RequireData(recorder, postedRun is not null, "لا يوجد تشغيل مرحّل بلا طلب عكس معلّق على خادم العرض", "The demo has no posted run without a waiting reversal request.");
        var line = Pay56One($"select \"Id\" from \"SalaryLines\" where \"PayrollRunId\" = '{postedRun}' order by \"Id\" limit 1");
        var demoBefore = Pay56DemoData();
        var output = Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan56", "vps", "screens");
        var results = new List<Ux067PageResult>();
        var repeats = new List<string>();
        try
        {
            // What the screens show: a draft run of the checks, and a request that waits on a demo posted run.
            var draft = await Pay56CreateRunAsync(processor, Pay56FreeMonth());
            var (requested, requestError, requestBody) = await Pay56PostAsync(admin, $"runs/{postedRun}/reversal-requests", new { reason = Pay56ReversalReason });
            var request = Pay56IdOf(requestBody);
            Assert.True(requested == 200 && request.Length == 36, $"The request by {Pay56Admin} answered {requested}: {requestError}");
            var requester = Pay56One($"select \"RequestedBy\" from \"PayrollReversalRequests\" where \"Id\" = '{request}'");

            Ux067KeyPage[] processorPages =
            [
                new("runs", Pay56Runs),
                new("run-draft-with-its-acts", $"{Pay56Runs}/Details/{draft}"),
                new("run-posted-with-its-request", $"{Pay56Runs}/Details/{postedRun}"),
                new("adjustment-form", "/ui/payroll/PayrollAdjustments/Create"),
                new("adjustments", "/ui/payroll/PayrollAdjustments"),
                new("payslips", "/ui/payroll/Ess/Payslips"),
            ];
            Ux067KeyPage[] approverPages =
            [
                new("reversal-requests", "/ui/payroll/PayrollReversalRequests"),
                new("reversal-requests-waiting", "/ui/payroll/PayrollReversalRequests?status=Pending"),
            ];
            (int Width, int Height)[] sizes = [(1366, 768), (390, 844)];

            // A page load, or the sign-in, fails when a network interface of the machine that runs the check changes under Chromium
            // (PLAN-54's note). Each session is opened once, each page is probed on its own, and a step the network cut is done again.
            async Task<T> OnThisMachinesNetworkAsync<T>(string step, Func<Task<T>> work)
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        return await work();
                    }
                    catch (Exception ex) when (attempt < 12 && Ux067PageProbe.IsClientNetworkCut(ex))
                    {
                        repeats.Add($"{step}: {ex.Message.Split('\n')[0]}");
                        await Task.Delay(TimeSpan.FromSeconds(3 + ((attempt * 7) % 23)));
                    }
                }
            }

            var playwright = await Playwright.CreateAsync();
            // Over HTTP/3 a connection whose local path changed stalls to the navigation timeout; over TCP it fails fast and is done again.
            var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Args = ["--disable-quic"] });
            var states = new List<string>();
            try
            {
                foreach (var (user, pages) in new[] { (Pay56Processor, processorPages), (Pay56Approver, approverPages) })
                {
                    var state = Path.Combine(Path.GetTempPath(), $"pay56-state-{Guid.NewGuid():N}.json");
                    states.Add(state);
                    await OnThisMachinesNetworkAsync($"sign-in {user}", async () =>
                    {
                        await Ux067PageProbe.SignInAndSaveStateAsync(browser, $"https://{AppDomain}", user, password, state);
                        return true;
                    });
                    var (probed, again) = await Ux067PageProbe.ProbeEachAsync(
                        browser, $"https://{AppDomain}", state, pages, sizes, Path.Combine(output, "screenshots"), attemptsPerPage: 12);
                    results.AddRange(probed);
                    repeats.AddRange(again);
                }
            }
            finally
            {
                await browser.DisposeAsync();
                playwright.Dispose();
                foreach (var state in states.Where(File.Exists)) File.Delete(state);
            }

            // What the server sends, read as text: Arabic, right-to-left, people by the registry's spans, the new acts.
            async Task<string> PageAsync(HttpClient client, string path)
            {
                using var page = await client.GetAsync($"https://{AppDomain}{path}");
                Assert.True(page.IsSuccessStatusCode, $"GET {path} answered {(int)page.StatusCode}");
                return WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            }

            var draftPage = await PageAsync(processor, $"{Pay56Runs}/Details/{draft}");
            var postedPage = await PageAsync(processor, $"{Pay56Runs}/Details/{postedRun}");
            var requestsPage = await PageAsync(approver, "/ui/payroll/PayrollReversalRequests");
            var form = await PageAsync(processor, "/ui/payroll/PayrollAdjustments/Create");

            // The payslip of a demo line: the four figures, and the net the formula gives. No amount goes into the evidence.
            using var payslip = await processor.GetAsync($"https://{AppDomain}{Pay56Api}/payslips/{line}");
            var payslipStatus = (int)payslip.StatusCode;
            var payslipBody = await payslip.Content.ReadAsStringAsync();
            var payslipAddsUp = false;
            var payslipHasTheAdjustments = false;
            if (payslipStatus == 200)
            {
                using var json = JsonDocument.Parse(payslipBody);
                var l = json.RootElement.GetProperty("line");
                payslipHasTheAdjustments = l.TryGetProperty("totalAdjustments", out var adjustments);
                payslipAddsUp = payslipHasTheAdjustments
                    && l.GetProperty("netPay").GetDecimal() == Math.Max(0m, l.GetProperty("grossPay").GetDecimal() - l.GetProperty("totalDeductions").GetDecimal() + adjustments.GetDecimal())
                    && json.RootElement.GetProperty("components").EnumerateArray().Any();
            }

            static string Visible(string html) =>
                Regex.Replace(Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase), "<[^>]+>", " ");
            var failures = results.Where(r => !r.Passes || r.Status != 200)
                .Select(r => $"{r.Name}: HTTP {r.Status}, {r.CspPolicies} policies, {r.Violations.Count} violations {string.Join("; ", r.Violations.Select(v => $"{v.Directive} {v.BlockedUri} {v.Source}"))} {r.Note}")
                .ToList();
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "csp-pages.json"), Ux067PageProbe.ToJson(results));
            recorder.WriteScenarioSteps(string.Join("\n", results.Select(r =>
                $"- {r.Name}: HTTP {r.Status}, {r.CspHeaders} CSP header(s), {r.CspPolicies} policies, {r.Violations.Count} violation(s)" + (r.Note is null ? "" : $" ({r.Note})"))));
            recorder.WriteScenarioDbChecks(new
            {
                draft,
                postedRun,
                request,
                repeats,
                pages = results.Select(r => new { r.Name, r.Status, r.CspHeaders, r.CspPolicies, violations = r.Violations.Count, r.Passes }),
                rightToLeft = draftPage.Contains("dir=\"rtl\"", StringComparison.Ordinal),
                arabic = draftPage.Contains("lang=\"ar\"", StringComparison.Ordinal),
                theDraftOffersItsCancellation = draftPage.Contains("data-hr-confirm-title=\"إلغاء تشغيل الرواتب\"", StringComparison.Ordinal),
                thePostedRunShowsItsRequest = postedPage.Contains(Pay56ReversalReason, StringComparison.Ordinal),
                requesterNamedByTheRegistry = postedPage.Contains($"data-hr-user-id=\"{requester}\"", StringComparison.OrdinalIgnoreCase),
                requesterIdPrinted = Visible(postedPage).Contains(requester, StringComparison.OrdinalIgnoreCase),
                theListOffersTheRejection = requestsPage.Contains("data-hr-confirm-title=\"رفض طلب العكس\"", StringComparison.Ordinal),
                theListNamesTheRequester = requestsPage.Contains($"data-hr-user-id=\"{requester}\"", StringComparison.OrdinalIgnoreCase),
                theListPrintsNoIdentifier = !Visible(requestsPage).Contains(requester, StringComparison.OrdinalIgnoreCase)
                    && !Visible(requestsPage).Contains(postedRun!, StringComparison.OrdinalIgnoreCase),
                theFormOffersThePostedRun = form.Contains($"value=\"{postedRun}\"", StringComparison.OrdinalIgnoreCase),
                payslip = payslipStatus,
                payslipHasTheAdjustments,
                payslipAddsUp
            });

            Assert.Equal(processorPages.Length + approverPages.Length, results.Count);        // every page,
            Assert.All(results, r => Assert.Equal(sizes.Length, r.Screenshots.Count));       // opened at both sizes
            Assert.True(failures.Count == 0, "Screens failing the CSP check:\n" + string.Join("\n", failures));
            Assert.Contains("dir=\"rtl\"", draftPage, StringComparison.Ordinal);
            Assert.Contains("lang=\"ar\"", draftPage, StringComparison.Ordinal);
            Assert.Contains("data-hr-confirm-title=\"إلغاء تشغيل الرواتب\"", draftPage, StringComparison.Ordinal);
            Assert.Contains(Pay56ReversalReason, postedPage, StringComparison.Ordinal);
            Assert.Contains($"data-hr-user-id=\"{requester}\"", postedPage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(requester, Visible(postedPage), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("طلبات العكس", Visible(requestsPage), StringComparison.Ordinal);
            Assert.Contains(Pay56ReversalReason, requestsPage, StringComparison.Ordinal);
            Assert.Contains("data-hr-confirm-title=\"رفض طلب العكس\"", requestsPage, StringComparison.Ordinal);
            Assert.Contains($"data-hr-user-id=\"{requester}\"", requestsPage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(requester, Visible(requestsPage), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(postedRun!, Visible(requestsPage), StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"value=\"{postedRun}\"", form, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(200, payslipStatus);
            Assert.True(payslipHasTheAdjustments, "The payslip does not carry the line's adjustments.");
            Assert.True(payslipAddsUp, "The payslip's net is not gross - deductions + adjustments.");
            Assert.Equal(demoBefore, Pay56DemoData());
        }
        finally
        {
            await Pay56CloseAsync(processor, approver);
        }

        Assert.Equal("0|0", Pay56OpenLeft());
        recorder.WriteResult("Pass");
    }
}
