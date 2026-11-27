using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-59 (§4, P-10): one monthly payroll cycle of agency A, by HTTP alone, one person per role. Every act is one call of the product's
/// API or one screen post (with the URL and the token the page renders); every check is an API read, written to <c>db-checks.json</c>.
/// No SQL. See <see cref="AcPay59FullCycleTests"/> for the fifteen steps.
/// </summary>
internal static partial class PayrollCycleDriver
{
    public const string Owner = "AC-PAY-59";
    public const string Law50Code = "LAW_50_2004";
    public const decimal AdjustmentAmount = 25_000m;
    private const string AttendanceUi = "ui/hr-core/DailyAttendanceRecords";
    private const string AdjustmentsUi = "ui/payroll/PayrollAdjustments";
    private static readonly string Hq = HRManagement.ServiceDefaults.AcceptanceSeedIds.HqOrgUnitId.ToString("D");

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<KeyValuePair<string, string>> F(params (string Key, string Value)[] fields) =>
        fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();

    /// <summary>A screen session (a bearer client that keeps its own cookies), for the screens the cycle posts to.</summary>
    private static async Task<AcceptanceUi> ScreenAsync(EvidenceRecorder recorder, string persona, string tag)
    {
        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D"));
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(E2eEnv.BaseUrl.TrimEnd('/') + "/"),
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new AcceptanceUi(http, recorder, persona, tag);
    }

    private static decimal Dec(JsonElement e, string property) => e.GetProperty(property).GetDecimal();

    private static Guid Id(JsonElement e, string property) => e.GetProperty(property).GetGuid();

    /// <summary>Every order of a batch, page after page.</summary>
    public static async Task<IReadOnlyList<CycleOrder>> OrdersAsync(PayrollApi api, string step, string batchId)
    {
        var orders = new List<CycleOrder>();
        for (var page = 1; ; page++)
        {
            var body = await api.ReadAsync($"{step}-p{page}", $"api/payroll/payment-batches/{batchId}/orders?page={page}", capture: page == 1);
            var items = body.GetProperty("orders").EnumerateArray().ToList();
            orders.AddRange(items.Select(o => new CycleOrder(
                Id(o, "id"), Id(o, "employeeId"), Id(o, "salaryLineId"), o.GetProperty("status").GetString() ?? "", Dec(o, "amount"),
                o.GetProperty("holdReason").ValueKind == JsonValueKind.String ? o.GetProperty("holdReason").GetString() : null,
                o.GetProperty("destinationPinned").GetBoolean(),
                o.GetProperty("fileSequence").ValueKind == JsonValueKind.Number ? o.GetProperty("fileSequence").GetInt32() : null)));
            if (items.Count == 0 || orders.Count >= body.GetProperty("total").GetInt32())
                return orders;
        }
    }

    public static async Task<IReadOnlyList<CycleFile>> FilesAsync(PayrollApi api, string step, string batchId)
    {
        var body = await api.ReadAsync(step, $"api/payroll/payment-batches/{batchId}/files");
        return body.EnumerateArray().Select(f => new CycleFile(
            Id(f, "id"), f.GetProperty("sequence").GetInt32(), f.GetProperty("kind").GetString() ?? "", f.GetProperty("controlCount").GetInt32(),
            Dec(f, "controlTotal"))).ToList();
    }

    /// <summary>The working days of a month on the attendance calendar's week (Friday and Saturday off).</summary>
    private static IEnumerable<DateOnly> WorkingDays(DateOnly month)
    {
        for (var d = month; d < month.AddMonths(1); d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday))
                yield return d;
        }
    }

    /// <summary>
    /// Runs the cycle. <paramref name="checks"/> collects every check (the API read and what it showed); <paramref name="timing"/> each
    /// step's milliseconds. Returns the cycle's figures for the evidence's <c>after.json</c>.
    /// </summary>
    public static async Task<object> RunAsync(EvidenceRecorder recorder, Dictionary<string, object?> checks, Dictionary<string, long> timing)
    {
        var clock = Stopwatch.StartNew();
        void Lap(string step)
        {
            timing[step] = clock.ElapsedMilliseconds;
            recorder.RecordTiming(step + "Ms", clock.ElapsedMilliseconds);
            clock.Restart();
        }

        using var processor = await PayrollApi.PayrollAsync(recorder, "payroll-processor", "59");
        using var approver = await PayrollApi.PayrollAsync(recorder, "payroll-approver", "59");
        using var approver2 = await PayrollApi.PayrollAsync(recorder, AcceptancePayrollReview.SecondApprover, "59");
        using var poster = await PayrollApi.PayrollAsync(recorder, "a-payroll-poster", "59");
        using var central = await PayrollApi.AttendanceAsync(recorder, "central-a", "59");
        var centralUi = await ScreenAsync(recorder, "central-a", "59");
        var processorUi = await ScreenAsync(recorder, "payroll-processor", "59");
        var approverUi = await ScreenAsync(recorder, "payroll-approver", "59");

        // ------------------------------------------------------------ the month (D-PAY-22) and the run's scope
        var (month, runId, refused) = await CycleMonthAsync(processor, central, recorder, "month", Owner, AcceptancePayrollMonths.CycleStack);
        var ym = AcceptancePayrollMonths.Ym(month);
        var mark = "59" + month.ToString("yyMM", CultureInfo.InvariantCulture);
        string? changeId = null;
        string? pendingAdjustment = null;
        var posted = false;
        try
        {
            var before = await processor.ReadAsync("scope", $"api/payroll/runs/{runId}/attendance-status");
            var scope = before.GetProperty("missingAttendance").EnumerateArray().Select(e => e.GetGuid()).ToList();
            var inScope = before.GetProperty("employeesInScope").GetInt32();
            Assert.True(scope.Count == inScope && inScope >= 3,
                $"A month nobody closed lists its whole scope as missing: {scope.Count} of {inScope} (gate {before.GetProperty("gateMode").GetString()}).");
            Assert.Equal("Warn", before.GetProperty("gateMode").GetString());

            // Y, X and Z: three employees of the scope, in a fixed order (the ids' order), Z with no open salary change request.
            var ordered = scope.Order().ToList();
            var y = ordered[0];
            var x = ordered[1];
            // Z: from the third id on, the first with no open salary change request whose grade leaves room for a raise (step 3 finds it).
            var zCandidates = new List<Guid>();
            foreach (var candidate in ordered.Skip(2).Take(40))
            {
                var requests = await processor.ReadAsync($"z-requests-{candidate:N}"[..18], $"api/payroll/pay-profile-changes?employeeId={candidate:D}", capture: false);
                if (!requests.EnumerateArray().Any(r => r.GetProperty("state").GetString() is "PendingApproval" or "Scheduled"))
                    zCandidates.Add(candidate);
            }

            Assert.NotEmpty(zCandidates);
            Guid z = Guid.Empty;
            checks["month"] = new { month = ym, runId, refused, employeesInScope = inScope, missingBeforeTheClose = scope.Count, gateMode = "Warn", y, x, zCandidates = zCandidates.Count };
            recorder.WriteBefore(new
            {
                month = ym,
                runId,
                candidatesRefused = refused,
                employeesInScope = inScope,
                y,
                x,
                zCandidates = zCandidates.Count,
                cycleFrom = Environment.GetEnvironmentVariable(AcceptancePayrollMonths.CycleFromVariable) ?? "(default: 12 months before today)",
            });
            Lap("month");

            // ------------------------------------------------------------ 1. the framework in force in M
            var frameworks = await processor.ReadAsync("1-frameworks", $"api/payroll/frameworks?code={Law50Code}");
            var inForce = frameworks.EnumerateArray()
                // The engine's rule (PayrollConfigurationVersioning.ResolveGoverning): a published or superseded version whose dates cover the
                // period's first day. The provenance after the calculation confirms which version governed (step 5).
                .Where(f => f.GetProperty("status").GetString() is "Published" or "Superseded"
                            && DateOnly.Parse(f.GetProperty("effectiveFrom").GetString()!, CultureInfo.InvariantCulture) <= month
                            && (f.GetProperty("effectiveTo").ValueKind != JsonValueKind.String
                                || DateOnly.Parse(f.GetProperty("effectiveTo").GetString()!, CultureInfo.InvariantCulture) >= month))
                .OrderByDescending(f => f.GetProperty("effectiveFrom").GetString())
                .ThenByDescending(f => f.GetProperty("versionNumber").GetInt32())
                .FirstOrDefault();
            string frameworkId;
            string frameworkHow;
            if (inForce.ValueKind == JsonValueKind.Object)
            {
                frameworkId = inForce.GetProperty("id").GetGuid().ToString("D");
                frameworkHow = $"in force: {Law50Code} v{inForce.GetProperty("versionNumber").GetInt32()} from {inForce.GetProperty("effectiveFrom").GetString()}";
            }
            else
            {
                // None in force: AC-PAY-54a's flow — imported from the first day of M, approved, attested by a third person, published.
                var pack = await File.ReadAllTextAsync(Path.Combine(AcceptanceHarness.RepoRoot, "docs", "syr", "payroll-framework-packs", "law50-interim.json"));
                using var envelope = new StringContent("{\"pack\":" + pack + ",\"code\":\"" + Law50Code + "\",\"effectiveFrom\":\"" + Day(month) + "\"}",
                    System.Text.Encoding.UTF8, "application/json");
                using var imported = await processor.Http.PostAsync("api/payroll/frameworks/import", envelope);
                var importedText = await imported.Content.ReadAsStringAsync();
                recorder.CaptureHttp("59-1-import-payroll-processor", "POST", "/api/payroll/frameworks/import", (int)imported.StatusCode,
                    importedText.TrimStart().StartsWith('{') ? JsonSerializer.Deserialize<JsonElement>(importedText) : PayrollApi.Snip(importedText));
                Assert.True(imported.StatusCode == HttpStatusCode.Created, $"1: the import answered {(int)imported.StatusCode}: {PayrollApi.Snip(importedText)}");
                frameworkId = JsonSerializer.Deserialize<JsonElement>(importedText).GetProperty("frameworkId").GetGuid().ToString("D");
                foreach (var (api, act) in new[] { (approver, "approve"), (approver2, "legal-attest"), (approver, "publish") })
                {
                    var (status, _, text) = await api.PostAsync($"1-{act}", $"api/payroll/frameworks/{frameworkId}/{act}");
                    Assert.True(status == HttpStatusCode.OK, $"1: {act} answered {(int)status}: {PayrollApi.Snip(text)}");
                }

                frameworkHow = $"imported from {Day(month)}, approved, attested, published";
            }

            checks["1-framework"] = new { read = $"GET api/payroll/frameworks?code={Law50Code}", frameworkId, how = frameworkHow };
            Lap("1-framework");

            // ------------------------------------------------------------ 2. accounts: everyone verified but X; X's new account pending
            var verified = 0;
            var alreadyActive = 0;
            var accountCalls = new List<string>();
            var readCaptured = false;
            foreach (var employee in ordered)
            {
                var path = $"api/payroll/employees/{employee:D}/bank-accounts";
                var accounts = await processor.ReadAsync("2-accounts-first", path, capture: !readCaptured);
                readCaptured = true;
                var active = accounts.EnumerateArray().FirstOrDefault(a => a.GetProperty("status").GetString() == "Active");
                var pending = accounts.EnumerateArray().FirstOrDefault(a => a.GetProperty("status").GetString() == "PendingVerification");
                if (employee == x)
                {
                    if (active.ValueKind == JsonValueKind.Object)
                    {
                        var (retired, _, retiredText) = await approver.PostAsync("2-x-retire", $"{path}/{active.GetProperty("id").GetGuid():D}/retire",
                            new { reason = $"AC-PAY-59 {ym}: حساب من تنفيذ سابق يُحال قبل طلب حساب جديد" });
                        Assert.True(retired == HttpStatusCode.OK, $"2: retiring X's account answered {(int)retired}: {PayrollApi.Snip(retiredText)}");
                    }

                    if (pending.ValueKind == JsonValueKind.Object)
                    {
                        var (staleRejected, _, rejectedText) = await approver.PostAsync("2-x-reject-stale", $"{path}/{pending.GetProperty("id").GetGuid():D}/reject",
                            new { reason = $"AC-PAY-59 {ym}: طلب حساب متروك من تنفيذ سابق" });
                        Assert.True(staleRejected == HttpStatusCode.OK, $"2: rejecting X's stale request answered {(int)staleRejected}: {PayrollApi.Snip(rejectedText)}");
                    }

                    var (registeredX, registeredXBody, registeredXText) = await processor.PostAsync("2-x-register", path, new
                    {
                        accountKind = "Iban",
                        accountNumber = AcceptancePayrollAccounts.UniqueIban(employee.ToString("D"), mark),
                        bankNameAr = "المصرف التجاري السوري",
                        holderNameAr = "موظف اختبار القبول",
                    });
                    Assert.True(registeredX == HttpStatusCode.Created, $"2: registering X's account answered {(int)registeredX}: {PayrollApi.Snip(registeredXText)}");
                    checks["2-x-account"] = new { employee = x, accountId = registeredXBody.GetProperty("accountId").GetGuid(), state = "PendingVerification" };
                    continue;
                }

                if (active.ValueKind == JsonValueKind.Object)
                {
                    alreadyActive++;
                    continue;
                }

                var accountId = pending.ValueKind == JsonValueKind.Object ? pending.GetProperty("id").GetGuid().ToString("D") : null;
                var capture = verified == 0;
                if (accountId is null)
                {
                    var (registered, registeredBody, registeredText) = await processor.PostAsync("2-register", path, new
                    {
                        accountKind = "Iban",
                        accountNumber = AcceptancePayrollAccounts.UniqueIban(employee.ToString("D"), mark),
                        bankNameAr = "المصرف التجاري السوري",
                        holderNameAr = "موظف اختبار القبول",
                    }, capture);
                    Assert.True(registered == HttpStatusCode.Created, $"2: registering {employee} answered {(int)registered}: {PayrollApi.Snip(registeredText)}");
                    accountId = registeredBody.GetProperty("accountId").GetGuid().ToString("D");
                }

                var (verifiedStatus, _, verifiedText) = await approver.PostAsync("2-verify", $"{path}/{accountId}/verify",
                    new { verificationReference = $"AC-PAY-59-{ym}" }, capture);
                Assert.True(verifiedStatus == HttpStatusCode.OK, $"2: verifying {employee} answered {(int)verifiedStatus}: {PayrollApi.Snip(verifiedText)}");
                verified++;
                accountCalls.Add($"{employee:D}:{accountId}");
            }

            // The last verification's answer, captured (the bulk step captures its first and last calls).
            if (accountCalls.Count > 0)
            {
                var last = accountCalls[^1].Split(':');
                await processor.ReadAsync("2-accounts-last", $"api/payroll/employees/{last[0]}/bank-accounts");
            }

            var xAccounts = await processor.ReadAsync("2-x-accounts", $"api/payroll/employees/{x:D}/bank-accounts");
            Assert.Equal(["PendingVerification"], xAccounts.EnumerateArray().Select(a => a.GetProperty("status").GetString()).Where(s => s is "Active" or "PendingVerification"));
            checks["2-accounts"] = new { verifiedNow = verified, alreadyActive, employeesInScope = ordered.Count, x, xAccountStates = "PendingVerification only" };
            Lap("2-accounts");

            // ------------------------------------------------------------ 3. Z's salary change (P-4: from the first day of next month)
            // D-53-1 refuses a date before the current month and M has ended, so the change is dated next month: decided by two people,
            // Scheduled, it reaches no part of M (Z's line is at his salary in force in M), and it is cancelled in finally.
            var effective = AcceptancePayrollMonths.CurrentMonth.AddMonths(1);
            object ChangeBody(Guid employee, decimal salary, string why) => new
            {
                employeeId = employee,
                proposedBaseSalary = salary,
                effectiveFrom = Day(effective),
                reason = $"AC-PAY-59 {ym}: {why}",
                legalReference = "قرار اختبار القبول 59",
            };

            // A raise inside the grade's bounds, by the request's own answers (no SQL): a salary of 1 is refused with the bounds, and the
            // grade's maximum is asked; a salary already at the maximum is refused as no change, and the next candidate is tried. A grade
            // with no bounds takes the probe, which names the salary in force and is cancelled at once; a thousand more is then asked.
            decimal current;
            decimal proposed = 0;
            JsonElement requestedBody = default;
            var atMaximum = new List<Guid>();
            foreach (var candidate in zCandidates)
            {
                var (probe, probeBody, probeText) = await processor.PostAsync("3-probe-bounds", "api/payroll/pay-profile-changes", ChangeBody(candidate, 1m, "قراءة حدود الدرجة"));
                var bounds = Regex.Match(PayrollApi.Error(probeBody), @"الحد الأدنى ([0-9.]+) والحد الأعلى ([0-9.]+)");
                if (probe == HttpStatusCode.BadRequest && bounds.Success)
                {
                    proposed = decimal.Parse(bounds.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                else
                {
                    Assert.True(probe == HttpStatusCode.Created, $"3: the probe answered {(int)probe}: {PayrollApi.Snip(probeText)}");
                    var probeId = probeBody.GetProperty("requestId").GetGuid().ToString("D");
                    var probeRead = await processor.ReadAsync("3-probe-read", $"api/payroll/pay-profile-changes/{probeId}");
                    var (cancelledProbe, _, cancelledProbeText) = await processor.PostAsync("3-probe-cancel", $"api/payroll/pay-profile-changes/{probeId}/cancel",
                        new { reason = "AC-PAY-59: طلب قراءة الراتب فقط" });
                    Assert.True(cancelledProbe == HttpStatusCode.OK, $"3: cancelling the probe answered {(int)cancelledProbe}: {PayrollApi.Snip(cancelledProbeText)}");
                    proposed = Dec(probeRead, "currentBaseSalary") + 1000m;
                }

                var (requested, body, requestedText) = await processor.PostAsync("3-request", "api/payroll/pay-profile-changes", ChangeBody(candidate, proposed, "زيادة راتب من الشهر القادم (D-53-1)"));
                if (requested == HttpStatusCode.BadRequest && requestedText.Contains("لم يُقترح تغيير", StringComparison.Ordinal))
                {
                    atMaximum.Add(candidate);
                    continue;
                }

                Assert.True(requested == HttpStatusCode.Created, $"3: the request answered {(int)requested}: {PayrollApi.Snip(requestedText)}");
                z = candidate;
                requestedBody = body;
                break;
            }

            Assert.True(z != Guid.Empty, $"3: none of {zCandidates.Count} candidates has room for a raise in his grade.");
            changeId = requestedBody.GetProperty("requestId").GetGuid().ToString("D");
            Assert.Equal("PendingApproval", requestedBody.GetProperty("state").GetString());

            // The requester cannot decide it (no approval role); another person approves it → Scheduled (the date is after today).
            var (selfApproval, _, _) = await processor.PostAsync("3-approve-requester-negative", $"api/payroll/pay-profile-changes/{changeId}/approve", new { note = "AC-PAY-59" });
            Assert.Equal(HttpStatusCode.Forbidden, selfApproval);
            var (approvedChange, approvedChangeBody, approvedChangeText) = await approver.PostAsync("3-approve", $"api/payroll/pay-profile-changes/{changeId}/approve", new { note = "AC-PAY-59: يسري من الشهر القادم" });
            Assert.True(approvedChange == HttpStatusCode.OK, $"3: the approval answered {(int)approvedChange}: {PayrollApi.Snip(approvedChangeText)}");
            var change = await processor.ReadAsync("3-change", $"api/payroll/pay-profile-changes/{changeId}");
            current = Dec(change, "currentBaseSalary");
            Assert.Equal("Scheduled", change.GetProperty("state").GetString());
            Assert.Equal(Day(effective), change.GetProperty("effectiveFrom").GetString());
            Assert.NotEqual(change.GetProperty("requestedBy").GetGuid(), change.GetProperty("decidedBy").GetGuid());
            Assert.Equal(JsonValueKind.Null, change.GetProperty("appliedProfileId").ValueKind);
            proposed = Dec(change, "proposedBaseSalary");
            Assert.True(proposed > current, $"3: the request is not a raise ({current} → {proposed}).");
            checks["3-salary-change"] = new
            {
                read = $"GET api/payroll/pay-profile-changes/{changeId}",
                z,
                candidatesAtTheirMaximum = atMaximum,
                state = "Scheduled",
                effectiveFrom = Day(effective),
                currentBaseSalary = current,
                proposedBaseSalary = proposed,
                isARaise = proposed > current,
                requesterIsNotDecider = true,
                requesterApproval = (int)selfApproval,
                appliedProfileId = (string?)null,
                why = "D-53-1: a change is dated from the current month on; M has ended (P-4)",
            };
            _ = approvedChangeBody;
            Lap("3-salary-change");

            // ------------------------------------------------------------ 4. attendance of M, closed for the whole agency
            var absentDays = WorkingDays(month).Take(2).ToList();
            foreach (var day in absentDays)
            {
                var (created, createdBody) = await centralUi.PostWithStatusAsync($"4-absent-{day.Day:D2}", $"{AttendanceUi}/Create", $"{AttendanceUi}/Create",
                    F(("EmployeeId", y.ToString("D")), ("AttendanceDate", Day(day)), ("Status", "Absent"), ("Notes", $"AC-PAY-59 {ym}: غياب")));
                Assert.True(created == HttpStatusCode.OK, $"4: Y's absence on {Day(day)} answered {(int)created}: {PayrollApi.Snip(createdBody)}");
            }

            var (bulk, bulkBody) = await centralUi.PostWithStatusAsync("4-bulk-present", $"{AttendanceUi}/BulkEntry", $"{AttendanceUi}/BulkEntry",
                F(("orgUnitId", Hq), ("month", ym), ("defaultStatus", "Present")));
            Assert.True(bulk == HttpStatusCode.OK && bulkBody.Contains("الإدخال الجماعي: ", StringComparison.Ordinal),
                $"4: the bulk entry of {ym} from agency A's root answered {(int)bulk}: {PayrollApi.Snip(bulkBody)}");
            var (closed, closedBody, closedText) = await central.PostAsync("4-close", $"{PeriodsApi}/{ym}/close");
            Assert.True(closed == HttpStatusCode.OK, $"4: closing {ym} for the whole agency answered {(int)closed}: {PayrollApi.Snip(closedText)}");
            var closedEmployees = closedBody.GetProperty("closedEmployees").GetInt32();
            var period = await central.ReadAsync("4-period", $"{PeriodsApi}/{ym}");
            var ySummary = period.GetProperty("summaries").EnumerateArray().Single(s => s.GetProperty("employeeId").GetGuid() == y);
            Assert.Equal(2, ySummary.GetProperty("absentDays").GetInt32());

            // Payroll receives one summary per employee closed; wait for all of them.
            var receiptsPath = $"api/payroll/attendance/receipts?periodStart={Day(month)}";
            var waited = Stopwatch.StartNew();
            JsonElement receipts = default;
            while (waited.Elapsed < TimeSpan.FromMinutes(10))
            {
                receipts = await processor.ReadAsync("4-receipts", receiptsPath, capture: false);
                if (receipts.GetProperty("count").GetInt32() >= closedEmployees)
                    break;
                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            receipts = await processor.ReadAsync("4-receipts-final", receiptsPath);
            Assert.Equal(closedEmployees, receipts.GetProperty("count").GetInt32());
            var yReceipt = receipts.GetProperty("receipts").EnumerateArray().Single(r => r.GetProperty("employeeId").GetGuid() == y);
            Assert.Equal(2, yReceipt.GetProperty("latestAbsentDays").GetInt32());
            checks["4-attendance"] = new
            {
                reads = new[] { $"GET {PeriodsApi}/{ym}", $"GET {receiptsPath}" },
                yAbsentDays = absentDays.Select(Day),
                closedEmployees,
                periodStatus = period.GetProperty("status").GetString(),
                yAbsentDaysInTheSummary = 2,
                receipts = receipts.GetProperty("count").GetInt32(),
                receiptsWaitedSeconds = (int)waited.Elapsed.TotalSeconds,
            };
            Lap("4-attendance");

            // ------------------------------------------------------------ 5. calculate and validate
            async Task CalculateAndValidateAsync(string step)
            {
                var (calc, calcBody, calcText) = await processor.PostAsync($"{step}-calculate", $"api/payroll/runs/{runId}/calculate");
                Assert.True(calc == HttpStatusCode.OK, $"{step}: calculate answered {(int)calc}: {PayrollApi.Error(calcBody)} {PayrollApi.Snip(calcText)}");
                var (valid, validBody, validText) = await processor.PostAsync($"{step}-validate", $"api/payroll/runs/{runId}/validate");
                Assert.True(valid == HttpStatusCode.OK, $"{step}: validate answered {(int)valid}: {PayrollApi.Error(validBody)} {PayrollApi.Snip(validText)}");
            }

            await CalculateAndValidateAsync("5");
            var status5 = await processor.ReadAsync("5-attendance-status", $"api/payroll/runs/{runId}/attendance-status");
            var missing = status5.GetProperty("missingAttendance").EnumerateArray().Select(e => e.GetGuid()).ToList();
            Assert.True(missing.Count == 0, $"5: after the close, {missing.Count} employee(s) of the run have no closed attendance: {string.Join(", ", missing.Take(10))}");
            var run5 = await processor.ReadAsync("5-run", $"api/payroll/runs/{runId}");
            JsonElement LineOf(JsonElement run, Guid employee) =>
                run.GetProperty("salaryLines").EnumerateArray().Single(l => l.GetProperty("employeeId").GetGuid() == employee);
            async Task<JsonElement> PayslipAsync(string step, JsonElement line, bool capture = true) =>
                await processor.ReadAsync(step, $"api/payroll/payslips/{line.GetProperty("id").GetGuid():D}", capture);
            static string ComponentType(JsonElement c) =>
                c.GetProperty("componentType").ValueKind == JsonValueKind.String
                    ? c.GetProperty("componentType").GetString() ?? ""
                    : ((string[])["BaseSalary", "Allowance", "Deduction", "Adjustment", "Tax"])[c.GetProperty("componentType").GetInt32()];

            var yPayslip = await PayslipAsync("5-payslip-y", LineOf(run5, y));
            var absenceName = $"خصم غياب — فترة الحضور {ym}";
            var absence = yPayslip.GetProperty("components").EnumerateArray()
                .Where(c => ComponentType(c) == "Deduction" && c.GetProperty("componentName").GetString() == absenceName)
                .ToList();
            Assert.True(absence.Count == 1 && Dec(absence[0], "amount") < 0, $"5: Y's line carries no absence deduction «{absenceName}».");
            // Two days' worth: the engine prices a day at the monthly base over 30 (Calendar30) or over the period's working days.
            var yBase = Dec(yPayslip.GetProperty("components").EnumerateArray().Single(c => ComponentType(c) == "BaseSalary"), "amount");
            var yDeduction = -Dec(absence[0], "amount");
            var weekdays = WorkingDays(month).Count();
            var twoDays = new[] { yBase * 2m / 30m, yBase * 2m / weekdays, yBase * 2m / (weekdays - 1), yBase * 2m / (weekdays - 2) };
            Assert.True(twoDays.Any(expected => Math.Abs(expected - yDeduction) <= 1m),
                $"5: Y's absence deduction {yDeduction} is not two days of a monthly base of {yBase} (by 30 or by about {weekdays} working days).");
            Assert.Equal(JsonValueKind.String, absence[0].GetProperty("referenceId").ValueKind);
            var zPayslip = await PayslipAsync("5-payslip-z", LineOf(run5, z));
            var zBase = zPayslip.GetProperty("components").EnumerateArray().Single(c => ComponentType(c) == "BaseSalary");
            Assert.Equal(current, Dec(zBase, "amount"));
            var provenance = await processor.ReadAsync("5-provenance", $"api/payroll/runs/{runId}/provenance");
            Assert.Equal(frameworkId, provenance.GetProperty("payrollConfigurationId").GetGuid().ToString("D"));
            var readiness5 = await processor.ReadAsync("5-bank-readiness", $"api/payroll/runs/{runId}/bank-readiness");
            Assert.Equal([x], readiness5.GetProperty("employeesWithoutAccount").EnumerateArray().Select(e => e.GetGuid()).ToList());
            checks["5-calculated"] = new
            {
                reads = new[] { "attendance-status", "GET api/payroll/runs/{id}", "payslips/{line}", "provenance", "bank-readiness" },
                missingAttendance = missing,
                lines = run5.GetProperty("salaryLines").GetArrayLength(),
                yAbsenceDeduction = Dec(absence[0], "amount"),
                yBaseSalary = yBase,
                yAbsenceIsTwoDays = true,
                zBaseSalary = Dec(zBase, "amount"),
                zSalaryInForceInM = current,
                zChangeFromNextMonth = proposed,
                provenanceFramework = frameworkId,
                provenanceCode = provenance.TryGetProperty("frameworkCode", out var code) ? code.GetString() : null,
                bankReadinessWithoutAccount = new[] { x },
            };
            Lap("5-calculate");

            // ------------------------------------------------------------ 6. submit, and a rejection reopens the run at version 2
            var (submitted6, submitted6Body, _) = await processor.PostAsync("6-submit", $"api/payroll/runs/{runId}/submit-for-review");
            Assert.True(submitted6 == HttpStatusCode.OK, $"6: submit answered {(int)submitted6}: {PayrollApi.Error(submitted6Body)}");
            var (rejected, rejectedBody, _) = await approver.PostAsync("6-reject", $"api/payroll/runs/{runId}/reject",
                new { notes = $"AC-PAY-59 {ym}: أضف تسوية الموظف Y قبل الاعتماد" });
            Assert.True(rejected == HttpStatusCode.OK, $"6: reject answered {(int)rejected}: {PayrollApi.Error(rejectedBody)}");
            var run6 = await processor.ReadAsync("6-run", $"api/payroll/runs/{runId}");
            Assert.Equal(("Draft", 2), (AcceptancePayrollMonths.RunStatus(run6), run6.GetProperty("revision").GetInt32()));
            checks["6-rejected"] = new { read = "GET api/payroll/runs/{id}", status = "Draft", revision = 2 };
            Lap("6-reject");

            // ------------------------------------------------------------ 7. an adjustment of +25,000 for Y, by two people, then again
            var adjustmentsBefore = Dec(LineOf(run5, y), "totalAdjustments");
            var adjustmentReason = $"AC-PAY-59 {ym}: تسوية يدوية للموظف";
            var listPath = $"{AdjustmentsUi}?employeeId={y:D}";
            var pendingBefore = Regex.Matches(await approverUi.GetAsync("7-adjustments-before", listPath), @"PayrollAdjustments/Approve/([0-9a-fA-F-]{36})")
                .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var (entered, enteredBody) = await processorUi.PostWithStatusAsync("7-adjustment-enter", $"{AdjustmentsUi}/Create", $"{AdjustmentsUi}/Create",
                F(("EmployeeId", y.ToString("D")), ("Amount", AdjustmentAmount.ToString("0.00", CultureInfo.InvariantCulture)), ("AdjustmentReason", adjustmentReason)));
            Assert.True(entered == HttpStatusCode.OK, $"7: entering the adjustment answered {(int)entered}: {PayrollApi.Snip(enteredBody)}");
            var list = await approverUi.GetAsync("7-adjustments", listPath);
            var pendingIds = Regex.Matches(list, @"PayrollAdjustments/Approve/([0-9a-fA-F-]{36})").Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase).Where(id => !pendingBefore.Contains(id)).ToList();
            Assert.True(pendingIds.Count == 1, $"7: {pendingIds.Count} new adjustment(s) of Y to approve on the approver's page (expected the one just entered).");
            pendingAdjustment = pendingIds[0];
            var (decided, decidedBody) = await approverUi.PostWithStatusAsync("7-adjustment-approve", $"{AdjustmentsUi}/Approve/{pendingIds[0]}", listPath, F());
            Assert.True(decided == HttpStatusCode.OK, $"7: approving the adjustment answered {(int)decided}: {PayrollApi.Snip(decidedBody)}");
            var listAfter = await approverUi.GetAsync("7-adjustments-after", listPath);
            Assert.DoesNotContain($"PayrollAdjustments/Approve/{pendingIds[0]}", listAfter, StringComparison.OrdinalIgnoreCase);
            pendingAdjustment = null;

            await CalculateAndValidateAsync("7");
            var (submitted7, submitted7Body, _) = await processor.PostAsync("7-submit", $"api/payroll/runs/{runId}/submit-for-review");
            Assert.True(submitted7 == HttpStatusCode.OK, $"7: submit answered {(int)submitted7}: {PayrollApi.Error(submitted7Body)}");
            var run7 = await processor.ReadAsync("7-run", $"api/payroll/runs/{runId}");
            var yLine = LineOf(run7, y);
            // The adjustment's own effect (an input an earlier, failed execution freed would already be in step 5's line).
            Assert.Equal(AdjustmentAmount, Dec(yLine, "totalAdjustments") - adjustmentsBefore);
            Assert.Equal(Dec(yLine, "grossPay") - Dec(yLine, "totalDeductions") + Dec(yLine, "totalAdjustments"), Dec(yLine, "netPay"));
            Assert.Equal(("UnderReview", 2), (AcceptancePayrollMonths.RunStatus(run7), run7.GetProperty("revision").GetInt32()));
            checks["7-adjusted"] = new
            {
                read = "GET api/payroll/runs/{id}",
                adjustmentId = pendingIds[0],
                enteredBy = "payroll-processor",
                approvedBy = "payroll-approver",
                y = new { gross = Dec(yLine, "grossPay"), deductions = Dec(yLine, "totalDeductions"), adjustmentsBefore, adjustments = Dec(yLine, "totalAdjustments"), net = Dec(yLine, "netPay") },
                validatedWithANonZeroAdjustment = true,
            };
            Lap("7-adjust");

            // ------------------------------------------------------------ 8. the approvals the amount asks for
            var (approved8, approved8Body, _) = await approver.PostAsync("8-approve", $"api/payroll/runs/{runId}/approve");
            Assert.True(approved8 == HttpStatusCode.OK, $"8: approve answered {(int)approved8}: {PayrollApi.Error(approved8Body)}");
            var run8a = await processor.ReadAsync("8-run-first-approval", $"api/payroll/runs/{runId}");
            var required = run8a.GetProperty("approvalsRequired").GetInt32();
            if (required == 2)
            {
                Assert.Equal(("UnderReview", 1), (AcceptancePayrollMonths.RunStatus(run8a), run8a.GetProperty("approvalsGiven").GetInt32()));
                var (approvedTwice, approvedTwiceBody, _) = await approver2.PostAsync("8-approve-second", $"api/payroll/runs/{runId}/approve");
                Assert.True(approvedTwice == HttpStatusCode.OK, $"8: the second approval answered {(int)approvedTwice}: {PayrollApi.Error(approvedTwiceBody)}");
            }

            var run8 = await processor.ReadAsync("8-run", $"api/payroll/runs/{runId}");
            Assert.Equal("Approved", AcceptancePayrollMonths.RunStatus(run8));
            Assert.Equal(required, run8.GetProperty("approvalsGiven").GetInt32());
            var approvedTotal = Dec(run8, "totalNetPay");
            checks["8-approved"] = new { read = "GET api/payroll/runs/{id}", approvalsRequired = required, approvalsGiven = run8.GetProperty("approvalsGiven").GetInt32(), totalNetPay = approvedTotal };
            Lap("8-approve");

            // ------------------------------------------------------------ 9. the independent review by two auditors
            var review = await AcceptancePayrollReview.ReviewAsync(recorder, runId, "59-9");
            checks["9-review"] = review;
            Lap("9-review");

            // ------------------------------------------------------------ 10. the posting: one batch, X held NO-ACCOUNT, the others Draft
            var (postedStatus, postedBody, postedText) = await poster.PostAsync("10-post", $"api/payroll/runs/{runId}/post");
            Assert.True(postedStatus == HttpStatusCode.OK, $"10: post answered {(int)postedStatus}: {PayrollApi.Error(postedBody)} {PayrollApi.Snip(postedText)}");
            posted = true;
            var run10 = await processor.ReadAsync("10-run", $"api/payroll/runs/{runId}");
            Assert.Equal("Posted", AcceptancePayrollMonths.RunStatus(run10));
            Assert.Equal(approvedTotal, Dec(run10, "totalNetPay"));
            var batchId = run10.GetProperty("paymentBatchId").GetGuid().ToString("D");
            var batch10 = await processor.ReadAsync("10-batch", $"api/payroll/payment-batches/{batchId}");
            Assert.Equal("Pending", batch10.GetProperty("status").GetString());
            var orders10 = await OrdersAsync(processor, "10-orders", batchId);
            var positiveLines = run10.GetProperty("salaryLines").EnumerateArray().Count(l => Dec(l, "netPay") > 0);
            Assert.Equal(positiveLines, orders10.Count);
            var x10 = orders10.Single(o => o.EmployeeId == x);
            Assert.Equal(("Held", "NO-ACCOUNT", false), (x10.Status, x10.HoldReason, x10.DestinationPinned));
            Assert.All(orders10.Where(o => o.EmployeeId != x), o => Assert.Equal(("Draft", true), (o.Status, o.DestinationPinned)));
            var held10 = await processor.ReadAsync("10-held", $"api/payroll/payment-batches/{batchId}/held");
            var heldRows = held10.EnumerateArray().Select(h => (Employee: h.GetProperty("employeeId").GetGuid(), Reason: h.GetProperty("reason").GetString())).ToList();
            Assert.Equal([(x, (string?)"NO-ACCOUNT")], heldRows);
            checks["10-posted"] = new
            {
                reads = new[] { "GET api/payroll/runs/{id}", "GET payment-batches/{id}", "GET payment-batches/{id}/orders", "GET payment-batches/{id}/held" },
                status = "Posted",
                totalNetPay = Dec(run10, "totalNetPay"),
                totalAsApproved = true,
                batchId,
                batchStatus = "Pending",
                orders = orders10.Count,
                draft = orders10.Count(o => o.Status == "Draft"),
                held = heldRows.Select(h => new { h.Employee, h.Reason }),
            };
            Lap("10-post");

            // ------------------------------------------------------------ 11. processing: one Initial file without X; a second process makes none
            var (processed, processedBody, processedText) = await poster.PostAsync("11-process", $"api/payroll/payment-batches/{batchId}/process");
            Assert.True(processed == HttpStatusCode.OK, $"11: process answered {(int)processed}: {PayrollApi.Error(processedBody)} {PayrollApi.Snip(processedText)}");
            var orders11 = await OrdersAsync(processor, "11-orders", batchId);
            var sent11 = orders11.Where(o => o.EmployeeId != x).ToList();
            Assert.All(sent11, o => Assert.Equal(("Submitted", (int?)1), (o.Status, o.FileSequence)));
            var x11 = orders11.Single(o => o.EmployeeId == x);
            Assert.Equal(("Held", (int?)null), (x11.Status, x11.FileSequence));
            var files11 = await FilesAsync(poster, "11-files", batchId);
            var initial = Assert.Single(files11);
            Assert.Equal(("Initial", sent11.Count, sent11.Sum(o => o.Amount)), (initial.Kind, initial.ControlCount, initial.ControlTotal));
            var (csv, csvEmployees) = await FileRowsAsync(poster, recorder, "11-file-initial", batchId, initial.Id);
            Assert.DoesNotContain(x11.Id, csv);
            Assert.DoesNotContain(x, csvEmployees);
            Assert.Equal(sent11.Select(o => o.Id).Order(), csv.Order());

            // AC-PAY-59b: a second sending of the same batch makes no second file, and X's order is still not sent.
            var (again, againBody, _) = await poster.PostAsync("11-process-again-negative", $"api/payroll/payment-batches/{batchId}/process");
            var filesAgain = await FilesAsync(poster, "11-files-again", batchId);
            var xAgain = (await OrdersAsync(processor, "11-orders-again", batchId)).Single(o => o.EmployeeId == x);
            Assert.True(again is HttpStatusCode.Conflict or HttpStatusCode.BadRequest || (again == HttpStatusCode.OK && filesAgain.Count == 1),
                $"11 (59b): the second process answered {(int)again}: {PayrollApi.Error(againBody)}");
            Assert.Single(filesAgain);
            Assert.Equal(("Held", (int?)null), (xAgain.Status, xAgain.FileSequence));
            checks["11-processed"] = new
            {
                reads = new[] { "GET payment-batches/{id}/orders", "GET payment-batches/{id}/files", "GET payment-batches/{id}/files/{fileId} (order ids only)" },
                submitted = sent11.Count,
                initialFile = new { initial.Sequence, initial.Kind, initial.ControlCount, initial.ControlTotal, ordersInTheFile = csv.Count, heldOrderInTheFile = false },
                acPay59b = new { secondProcess = (int)again, error = PayrollApi.Error(againBody), files = filesAgain.Count, xStill = xAgain.Status },
            };
            Lap("11-process");

            // ------------------------------------------------------------ 12. X's account verified: NO-ACCOUNT lifted, re-pinned
            var xPending = (await processor.ReadAsync("12-x-accounts", $"api/payroll/employees/{x:D}/bank-accounts")).EnumerateArray()
                .Single(a => a.GetProperty("status").GetString() == "PendingVerification").GetProperty("id").GetGuid();
            var (verifiedX, verifiedXBody, _) = await approver.PostAsync("12-verify-x", $"api/payroll/employees/{x:D}/bank-accounts/{xPending:D}/verify",
                new { verificationReference = $"AC-PAY-59-{ym}-X" });
            Assert.True(verifiedX == HttpStatusCode.OK, $"12: verifying X's account answered {(int)verifiedX}: {PayrollApi.Error(verifiedXBody)}");
            var x12 = (await OrdersAsync(processor, "12-orders", batchId)).Single(o => o.EmployeeId == x);
            Assert.True(x12.DestinationPinned && x12.HoldReason != "NO-ACCOUNT" && x12.FileSequence is null,
                $"12: X's order after the verification: {x12.Status} {x12.HoldReason} pinned={x12.DestinationPinned}");
            var readiness12 = await processor.ReadAsync("12-bank-readiness", $"api/payroll/runs/{runId}/bank-readiness");
            Assert.Empty(readiness12.GetProperty("employeesWithoutAccount").EnumerateArray());
            checks["12-x-verified"] = new { reads = new[] { "orders", "bank-readiness" }, xOrder = new { x12.Status, x12.HoldReason, x12.DestinationPinned }, bankReadinessWithoutAccount = Array.Empty<Guid>() };
            Lap("12-verify-x");

            // ------------------------------------------------------------ 13. submit-held: a HeldRelease file with X's order alone
            var (released, releasedBody, releasedText) = await poster.PostAsync("13-submit-held", $"api/payroll/payment-batches/{batchId}/submit-held");
            Assert.True(released == HttpStatusCode.OK, $"13: submit-held answered {(int)released}: {PayrollApi.Error(releasedBody)} {PayrollApi.Snip(releasedText)}");
            var files13 = await FilesAsync(poster, "13-files", batchId);
            Assert.Equal(2, files13.Count);
            var heldRelease = files13.Single(f => f.Sequence == 2);
            var x13 = (await OrdersAsync(processor, "13-orders", batchId)).Single(o => o.EmployeeId == x);
            Assert.Equal(("HeldRelease", 1, x13.Amount), (heldRelease.Kind, heldRelease.ControlCount, heldRelease.ControlTotal));
            Assert.Equal(("Submitted", (int?)2), (x13.Status, x13.FileSequence));
            var (csv13, csv13Employees) = await FileRowsAsync(poster, recorder, "13-file-held-release", batchId, heldRelease.Id);
            Assert.Equal([x13.Id], csv13);
            Assert.Equal([x], csv13Employees);
            checks["13-held-release"] = new { file = new { heldRelease.Sequence, heldRelease.Kind, heldRelease.ControlCount, heldRelease.ControlTotal }, ordersInTheFile = csv13 };
            Lap("13-submit-held");

            // ------------------------------------------------------------ 14. the payslips equal the orders
            var orders14 = await OrdersAsync(processor, "14-orders", batchId);
            var byLine = orders14.ToDictionary(o => o.SalaryLineId);
            var lines14 = run10.GetProperty("salaryLines").EnumerateArray().ToList();
            var payslips = 0;
            var mismatches = new List<string>();
            for (var i = 0; i < lines14.Count; i++)
            {
                var line = lines14[i];
                var payslip = await PayslipAsync(i == 0 ? "14-payslip-first" : "14-payslip-last", line, capture: i == 0 || i == lines14.Count - 1);
                payslips++;
                var net = Dec(payslip.GetProperty("line"), "netPay");
                var lineId = line.GetProperty("id").GetGuid();
                var expected = byLine.TryGetValue(lineId, out var order) ? order.Amount : 0m;
                if (net != expected || (order is not null && order.EmployeeId != line.GetProperty("employeeId").GetGuid()))
                    mismatches.Add($"{lineId:D}: payslip {net} order {expected}");
            }

            Assert.Empty(mismatches);
            Assert.Equal(Dec(run10, "totalNetPay"), orders14.Sum(o => o.Amount));
            // P-4: the posted line of Z (calculated again at step 7, after his change was approved) is at his salary in force in M.
            var zPosted = await PayslipAsync("14-payslip-z", LineOf(run10, z));
            var zPostedBase = Dec(zPosted.GetProperty("components").EnumerateArray().Single(c => ComponentType(c) == "BaseSalary"), "amount");
            Assert.Equal(current, zPostedBase);
            checks["14-payslips"] = new { zPostedBaseSalary = zPostedBase, zSalaryInForceInM = current, payslipsRead = payslips, orders = orders14.Count, sumOfOrders = orders14.Sum(o => o.Amount), totalNetPay = Dec(run10, "totalNetPay"), mismatches = mismatches.Count };
            Lap("14-payslips");

            // ------------------------------------------------------------ 15. the bank's confirmation, recorded by two people
            var channelReference = $"AC59-ACK-{ym}";
            var (acked, ackedBody, _) = await poster.PostAsync("15-ack", $"api/payroll/payment-batches/{batchId}/ack", new { channelReference });
            Assert.True(acked == HttpStatusCode.OK, $"15: ack answered {(int)acked}: {PayrollApi.Error(ackedBody)}");
            var acknowledged = (await OrdersAsync(processor, "15-orders-acked", batchId)).Where(o => o.Status == "Acknowledged").Sum(o => o.Amount);
            var (asked, askedBody, askedText) = await poster.PostAsync("15-settlement-request", $"api/payroll/payment-batches/{batchId}/settlement-requests",
                new { statementReference = $"AC59-STMT-{ym}", settledAmount = acknowledged, reason = $"AC-PAY-59 {ym}: كشف المصرف" });
            Assert.True(asked == HttpStatusCode.Accepted, $"15: the settlement request answered {(int)asked}: {PayrollApi.Snip(askedText)}");
            var requestId = askedBody.GetProperty("requestId").GetGuid().ToString("D");
            var (selfDecided, _, _) = await poster.PostAsync("15-decide-requester-negative", $"api/payroll/dual-control/{requestId}/decide", new { approve = true });
            Assert.Equal(HttpStatusCode.Forbidden, selfDecided);
            var (decided15, decided15Body, _) = await approver.PostAsync("15-decide", $"api/payroll/dual-control/{requestId}/decide",
                new { approve = true, notes = $"AC-PAY-59 {ym}: مطابق لكشف المصرف" });
            Assert.True(decided15 == HttpStatusCode.OK, $"15: the decision answered {(int)decided15}: {PayrollApi.Error(decided15Body)}");
            var request15 = await processor.ReadAsync("15-request", $"api/payroll/dual-control/{requestId}");
            var batch15 = await processor.ReadAsync("15-batch", $"api/payroll/payment-batches/{batchId}");
            var orders15 = await OrdersAsync(processor, "15-orders", batchId);
            Assert.Equal("Settled", batch15.GetProperty("status").GetString());
            Assert.All(orders15, o => Assert.Equal("Settled", o.Status));
            Assert.NotEqual(request15.GetProperty("requestedBy").GetGuid(), request15.GetProperty("decidedBy").GetGuid());
            checks["15-settled"] = new
            {
                reads = new[] { "GET dual-control/{id}", "GET payment-batches/{id}", "GET payment-batches/{id}/orders" },
                channelReference,
                statementReference = $"AC59-STMT-{ym}",
                settledAmount = acknowledged,
                requestStatus = request15.GetProperty("status").GetString(),
                requesterIsNotDecider = true,
                requesterDecision = (int)selfDecided,
                batchStatus = "Settled",
                ordersSettled = orders15.Count,
            };
            Lap("15-settle");

            recorder.RecordTiming("gatewayRateLimitWaits", PayrollApi.RateLimitWaits);
            return new
            {
                month = ym,
                runId,
                batchId,
                lines = lines14.Count,
                orders = orders15.Count,
                totalNetPay = Dec(run10, "totalNetPay"),
                approvalsRequired = required,
                held = x,
                initialFile = new { initial.ControlCount, initial.ControlTotal },
                heldReleaseFile = new { heldRelease.ControlCount, heldRelease.ControlTotal },
                settled = true,
                y,
                z,
            };
        }
        finally
        {
            // P-4: the change is cancelled whatever happened, so nothing applies later; a run not posted gives its month back (PLAN-56).
            if (changeId is not null)
            {
                var (read, change, _) = await processor.GetAsync("finally-change", $"api/payroll/pay-profile-changes/{changeId}");
                var state = read == HttpStatusCode.OK ? change.GetProperty("state").GetString() : $"not read ({(int)read})";
                if (state is "PendingApproval" or "Scheduled")
                {
                    var (cancelled, cancelledBody, _) = await processor.PostAsync("finally-change-cancel", $"api/payroll/pay-profile-changes/{changeId}/cancel",
                        new { reason = $"AC-PAY-59 {ym}: انتهت الدورة؛ لا يسري التغيير" });
                    state = cancelled == HttpStatusCode.OK
                        ? (await processor.ReadAsync("finally-change-after", $"api/payroll/pay-profile-changes/{changeId}")).GetProperty("state").GetString()
                        : $"cancel refused ({(int)cancelled}): {PayrollApi.Error(cancelledBody)}";
                }

                checks["finally-change"] = state;
            }

            // An adjustment entered and never approved is rejected, so no pending input of Y is left for a later execution.
            if (pendingAdjustment is not null)
            {
                var (rejectedStatus, _) = await approverUi.PostWithStatusAsync("finally-adjustment-reject", $"{AdjustmentsUi}/Reject/{pendingAdjustment}",
                    AdjustmentsUi, F(("note", $"AC-PAY-59 {ym}: فشلت الدورة")));
                checks["finally-adjustment"] = $"rejected ({(int)rejectedStatus})";
            }

            checks["finally-run"] = posted
                ? "posted (kept)"
                : await CancelUnlessPostedAsync(processor, approver, "finally-run", runId, $"AC-PAY-59 {ym}: فشلت الدورة قبل الترحيل");
        }
    }

    /// <summary>
    /// The order ids a stored bank file carries (<c>GET payment-batches/{id}/files/{fileId}</c>, as the poster). Read, never kept: the
    /// evidence holds the file's size and its count of order rows only, not its content.
    /// </summary>
    private static async Task<(IReadOnlyList<Guid> Orders, IReadOnlyList<Guid> Employees)> FileRowsAsync(PayrollApi poster, EvidenceRecorder recorder, string step, string batchId, Guid fileId)
    {
        var path = $"api/payroll/payment-batches/{batchId}/files/{fileId:D}";
        using var response = await poster.Http.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{step}: the file answered {(int)response.StatusCode}");
        // PaymentBatchFileBuilder: PayrollRunId,PeriodStart,PeriodEnd,Currency,EmployeeId,NationalId,EmployeeName,…,PaymentOrderId,Amount.
        // The order id is read from the right (the name is quoted and may hold a comma; the amount never does), the employee from the left.
        var rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .Where(row => !row.StartsWith("CONTROL,", StringComparison.Ordinal))
            .Select(row => row.Split(','))
            .ToList();
        var orders = rows.Select(cells => Guid.Parse(cells[^2])).ToList();
        var employees = rows.Select(cells => Guid.Parse(cells[4])).ToList();
        recorder.CaptureHttp($"59-{step}-{poster.Persona}", "GET", "/" + path, (int)response.StatusCode, new { bytes = text.Length, orderRows = orders.Count, content = "not kept" });
        return (orders, employees);
    }
}
