using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using Microsoft.Playwright;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-53 (P8, P-16) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers, as
/// <c>e2e-payroll-processor</c> and <c>e2e-payroll-approver</c> (the password is read into memory only; evidence goes under
/// <c>artifacts/plan53/vps</c> and is never committed). The checks change no pay data: the salary change they request is scheduled for
/// next month and cancelled before its date, the grade change is rejected, and the delete is refused. What stays on the server is two
/// closed requests (one cancelled, one rejected) with their audit rows; the active pay profiles are compared before and after by their
/// count and the checksum P0 recorded (employee and salary of every active profile).
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Iam53Processor = "e2e-payroll-processor";
    private const string Iam53Approver = "e2e-payroll-approver";
    private const string Iam53Marker = "PLAN-53 server check";
    private const string Iam53Reason = "تعديل الراتب الأساسي بعد تسوية الوضع الوظيفي للموظف";
    private const string Iam53GradeReason = "رفع الحد الأعلى للدرجة وفق جدول الرواتب المعدّل";
    private const string Iam53Legal = "القرار 53 لعام 2026";
    private const string Iam53CancelReason = "أُلغي الطلب بعد انتهاء التحقق على خادم العرض";
    private const string Iam53RejectNote = "الحد المقترح لا يطابق جدول الرواتب النافذ";
    private const string Iam53Changes = "/ui/payroll/PayProfileChanges";
    private const string Iam53ReferenceChanges = "/ui/payroll/PayrollReferenceChanges";

    private static EvidenceRecorder StartIam53(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"iam53-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan53", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    /// <summary>The active pay profiles as P0 measured them: their count, and an md5 of every employee with his salary.</summary>
    private static string Iam53Profiles() =>
        Sql("payroll_db",
            "select count(*) filter (where \"IsActive\") || '|' || count(*) || '|' || " +
            "md5(string_agg(\"EmployeeId\"::text || ':' || \"BaseSalary\"::text, ',' order by \"EmployeeId\") filter (where \"IsActive\")) " +
            "from \"EmployeePayProfiles\"").Single();

    private static string Iam53Open() =>
        Sql("payroll_db",
            "select (select count(*) from \"PayProfileChangeRequests\" where \"State\" in (0, 1)) || '|' || " +
            "(select count(*) from \"PayrollReferenceChangeRequests\" where \"State\" = 0)").Single();

    private static string Iam53Row(string table, string id) =>
        Sql("payroll_db", $"select count(*)::text || ':' || md5(coalesce(string_agg(t::text, '|'), '')) from \"{table}\" t where t.\"Id\" = '{id}'").Single();

    private static string Iam53State(string table, string id) =>
        Sql("payroll_db", $"select \"State\" from \"{table}\" where \"Id\" = '{id}'").SingleOrDefault() ?? "";

    private static async Task<(int Status, string Path, string Body)> Iam53PostAsync(
        HttpClient client, string path, string tokenPage, Dictionary<string, string>? fields = null)
    {
        var form = new Dictionary<string, string>(fields ?? []) { ["__RequestVerificationToken"] = await TokenAsync(client, tokenPage) };
        var (status, finalPath, body) = await PostFormAsync(client, path, form);
        return (status, finalPath, WebUtility.HtmlDecode(body));
    }

    /// <summary>The first day of next month on the service's clock (Localization:TimeZone, Asia/Damascus).</summary>
    private static string Iam53NextMonth()
    {
        var today = HRManagement.ServiceDefaults.Localization.HrLocalTime.ToLocal(DateTime.UtcNow, HRManagement.ServiceDefaults.Localization.HrLocalTime.DefaultZone);
        return new DateOnly(today.Year, today.Month, 1).AddMonths(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static long Iam53OutboxSequence() =>
        long.Parse(Sql("payroll_db", "select case when is_called then last_value else 0 end from \"OutboxMessage_SequenceNumber_seq\"").Single(), CultureInfo.InvariantCulture);

    private static string Iam53Money(string numeric, decimal delta) =>
        (decimal.Parse(numeric, CultureInfo.InvariantCulture) + delta).ToString("0.##", CultureInfo.InvariantCulture);

    [SkippableFact]
    public async Task AC_IAM_53_vps_the_migration_ran_and_each_payroll_role_writes_only_what_the_mapping_gives()
    {
        var recorder = StartIam53("policies",
            "Demo server: PAY053 is recorded and the two request tables have row security; as e2e-payroll-approver EmployeePayProfiles/Create, DeductionTypes/Edit and PayGrades/Delete answer 403; the processor cannot approve; no row is written");
        var migration = Sql("payroll_db", "select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" like '%PAY053_SalaryChangeRequests'").Single();
        var rowSecurity = Sql("payroll_db",
            "select string_agg(relname || ':' || relrowsecurity::text, ',' order by relname) from pg_class " +
            "where relname in ('PayProfileChangeRequests', 'PayrollReferenceChangeRequests')").Single();
        var password = DemoPassword();
        var (processor, _) = await SignInAsync(Iam53Processor, password, "/ui/payroll/");
        var (approver, _) = await SignInAsync(Iam53Approver, password, "/ui/payroll/");
        using (processor)
        using (approver)
        {
            var employee = Sql("payroll_db", "select \"EmployeeId\" from \"EmployeePayProfiles\" where \"IsActive\" order by \"EmployeeId\" limit 1").Single();
            var grade = Sql("payroll_db", "select \"Id\" from \"PayGrades\" order by \"GradeCode\" limit 1").Single();
            var before = string.Join(" / ",
                Iam53Profiles(),
                Sql("payroll_db", "select count(*) from \"PayProfileChangeRequests\"").Single(),
                Sql("payroll_db", "select count(*) from \"PayrollReferenceChangeRequests\"").Single(),
                Sql("payroll_db", "select count(*) || ':' || md5(coalesce(string_agg(t::text, '|' order by t.\"Id\"), '')) from \"DeductionTypes\" t").Single(),
                Iam53Row("PayGrades", grade));

            var deduction = Sql("payroll_db", "select \"Id\" from \"DeductionTypes\" order by \"Id\" limit 1").Single();
            var nextMonth = Iam53NextMonth();
            int processorPages;
            using (var list = await processor.GetAsync($"https://{AppDomain}{Iam53Changes}"))
                processorPages = (int)list.StatusCode;
            var (approverRequest, _, _) = await Iam53PostAsync(approver, "/ui/payroll/EmployeePayProfiles/Create", Iam53Changes, new()
            {
                ["EmployeeId"] = employee,
                ["ProposedBaseSalary"] = "1",
                ["EffectiveFrom"] = nextMonth,
                ["Reason"] = Iam53Reason,
                ["LegalReference"] = Iam53Legal
            });
            var (approverDeduction, _, _) = await Iam53PostAsync(approver, $"/ui/payroll/DeductionTypes/Edit/{deduction}", Iam53Changes, new()
            {
                ["Id"] = deduction,
                ["Code"] = "P53",
                ["NameAr"] = Iam53Marker
            });
            var (approverGrade, _, _) = await Iam53PostAsync(approver, $"/ui/payroll/PayGrades/Delete/{grade}", Iam53Changes, new()
            {
                ["Reason"] = Iam53GradeReason,
                ["LegalReference"] = Iam53Legal
            });
            var (processorApprove, _, _) = await Iam53PostAsync(processor, $"{Iam53Changes}/Approve/{Guid.NewGuid():D}", Iam53Changes, new() { ["note"] = Iam53Marker });
            var (processorReferenceApprove, _, _) = await Iam53PostAsync(processor, $"{Iam53ReferenceChanges}/Approve/{Guid.NewGuid():D}", Iam53Changes, new() { ["note"] = Iam53Marker });

            var after = string.Join(" / ",
                Iam53Profiles(),
                Sql("payroll_db", "select count(*) from \"PayProfileChangeRequests\"").Single(),
                Sql("payroll_db", "select count(*) from \"PayrollReferenceChangeRequests\"").Single(),
                Sql("payroll_db", "select count(*) || ':' || md5(coalesce(string_agg(t::text, '|' order by t.\"Id\"), '')) from \"DeductionTypes\" t").Single(),
                Iam53Row("PayGrades", grade));
            recorder.WriteScenarioDbChecks(new
            {
                migration,
                rowSecurity,
                processorPages,
                approverRequest,
                approverDeduction,
                approverGrade,
                processorApprove,
                processorReferenceApprove,
                before,
                after
            });

            Assert.Equal("1", migration);
            Assert.Equal("PayProfileChangeRequests:true,PayrollReferenceChangeRequests:true", rowSecurity);
            Assert.Equal(200, processorPages);
            Assert.Equal(403, approverRequest);
            Assert.Equal(403, approverDeduction);
            Assert.Equal(403, approverGrade);
            Assert.Equal(403, processorApprove);
            Assert.Equal(403, processorReferenceApprove);
            Assert.Equal(before, after);
        }

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_IAM_53_vps_a_salary_change_is_requested_scheduled_and_cancelled_and_no_pay_profile_changes()
    {
        var recorder = StartIam53("salary-change",
            "Demo server: the processor requests a raise from the first day of next month, Damascus date (pending; he cannot approve it), the approver approves (scheduled, nothing applied, the outbox sequence advanced) and it is cancelled; the active pay profiles are as before");
        var password = DemoPassword();
        var (processor, _) = await SignInAsync(Iam53Processor, password, "/ui/payroll/");
        var (approver, _) = await SignInAsync(Iam53Approver, password, "/ui/payroll/");
        using (processor)
        using (approver)
        {
            var profilesBefore = Iam53Profiles();
            var openBefore = Iam53Open();
            var from = Iam53NextMonth();
            var pick = Sql("payroll_db",
                "select p.\"EmployeeId\" || '|' || p.\"BaseSalary\" || '|' || (case when p.\"BaseSalary\" + 1000 <= g.\"MaxSalary\" then 1000 else -1000 end) " +
                "from \"EmployeePayProfiles\" p join \"PayGrades\" g on g.\"Id\" = p.\"PayGradeId\" " +
                "where p.\"IsActive\" and p.\"BaseSalary\" > 0 and g.\"MaxSalary\" > 0 and p.\"BaseSalary\" between g.\"MinSalary\" and g.\"MaxSalary\" " +
                "and (p.\"BaseSalary\" + 1000 <= g.\"MaxSalary\" or p.\"BaseSalary\" - 1000 >= g.\"MinSalary\") " +
                $"and p.\"EffectiveFrom\" < '{from}' " +
                "and not exists (select 1 from \"PayProfileChangeRequests\" r where r.\"EmployeeId\" = p.\"EmployeeId\" and r.\"State\" in (0, 1)) " +
                "order by p.\"EmployeeId\" limit 1").SingleOrDefault()?.Split('|');
            AcceptanceGate.RequireData(recorder, pick is { Length: 3 }, "لا يوجد ملف أجر فعّال بدرجة تسمح بالتغيير", "The demo has no active pay profile with a grade that leaves room for the change.");
            var employee = pick![0];
            var proposed = Iam53Money(pick[1], decimal.Parse(pick[2], CultureInfo.InvariantCulture));
            var profileBefore = Sql("payroll_db",
                $"select count(*)::text || ':' || md5(coalesce(string_agg(t::text, '|' order by t.\"Id\"), '')) from \"EmployeePayProfiles\" t where t.\"EmployeeId\" = '{employee}'").Single();

            // Through the API (the gateway's /ui/payroll path, the processor's session), so the request carries the check's marker
            // as its SourceReference; the reason and the legal reference are as a clerk would write them.
            int submitted;
            string submittedBody;
            using (var answer = await processor.PostAsync($"https://{AppDomain}/ui/payroll/api/payroll/pay-profile-changes",
                       System.Net.Http.Json.JsonContent.Create(new
                       {
                           employeeId = employee,
                           proposedBaseSalary = decimal.Parse(proposed, CultureInfo.InvariantCulture),
                           effectiveFrom = from,
                           reason = Iam53Reason,
                           legalReference = Iam53Legal,
                           sourceReference = Iam53Marker
                       })))
            {
                submitted = (int)answer.StatusCode;
                submittedBody = await answer.Content.ReadAsStringAsync();
            }

            var request = Regex.Match(submittedBody, "\"requestId\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"").Groups[1].Value;
            try
            {
                Assert.True(submitted == 201 && request.Length == 36, $"The request answered {submitted}: {(submittedBody.Length > 200 ? submittedBody[..200] : submittedBody)}");
                var marker = Sql("payroll_db", $"select coalesce(\"SourceReference\", '') || '|' || \"Source\" from \"PayProfileChangeRequests\" where \"Id\" = '{request}'").Single();
                int formPage;
                using (var page = await processor.GetAsync($"https://{AppDomain}/ui/payroll/EmployeePayProfiles/Create?employeeId={employee}"))
                    formPage = (int)page.StatusCode;
                var details = $"{Iam53Changes}/Details/{request}";
                var pending = Iam53State("PayProfileChangeRequests", request);
                var (ownApprove, _, _) = await Iam53PostAsync(processor, $"{Iam53Changes}/Approve/{request}", details, new() { ["note"] = Iam53Marker });
                var afterOwn = Iam53State("PayProfileChangeRequests", request);
                var sequenceBefore = Iam53OutboxSequence();
                var (approved, _, _) = await Iam53PostAsync(approver, $"{Iam53Changes}/Approve/{request}", details, new() { ["note"] = "موافق: ضمن حدود الدرجة" });
                var sequenceAfter = Iam53OutboxSequence();
                var scheduled = Iam53State("PayProfileChangeRequests", request);
                var profileAfterApproval = Sql("payroll_db",
                    $"select count(*)::text || ':' || md5(coalesce(string_agg(t::text, '|' order by t.\"Id\"), '')) from \"EmployeePayProfiles\" t where t.\"EmployeeId\" = '{employee}'").Single();
                var (cancelled, _, _) = await Iam53PostAsync(approver, $"{Iam53Changes}/Cancel/{request}", details, new() { ["reason"] = Iam53CancelReason });
                var closed = Iam53State("PayProfileChangeRequests", request);
                var decision = Sql("payroll_db",
                    $"select (\"RequestedBy\" <> \"DecidedBy\")::text || '|' || coalesce(\"AppliedProfileId\"::text, '') from \"PayProfileChangeRequests\" where \"Id\" = '{request}'").Single();
                var profilesAfter = Iam53Profiles();
                var openAfter = Iam53Open();
                recorder.WriteScenarioDbChecks(new
                {
                    employee,
                    request,
                    from,
                    submitted,
                    marker,
                    formPage,
                    pending,
                    ownApprove,
                    afterOwn,
                    approved,
                    scheduled,
                    outboxRowsWritten = sequenceAfter - sequenceBefore,
                    cancelled,
                    closed,
                    decision,
                    profileUnchanged = profileBefore == profileAfterApproval,
                    profilesBefore,
                    profilesAfter,
                    openBefore,
                    openAfter
                });

                Assert.Equal($"{Iam53Marker}|1", marker);   // the source is stored as its number: 1 = Api
                Assert.Equal(200, formPage);
                Assert.Equal("0", pending);
                Assert.Equal(403, ownApprove);
                Assert.Equal("0", afterOwn);
                Assert.Equal(200, approved);
                Assert.Equal("1", scheduled);
                Assert.True(sequenceAfter > sequenceBefore, $"The approval wrote no outbox row ({sequenceBefore} -> {sequenceAfter}).");
                Assert.Equal(profileBefore, profileAfterApproval);
                Assert.Equal(200, cancelled);
                Assert.Equal("4", closed);
                Assert.Equal("true|", decision);
                Assert.Equal(profilesBefore, profilesAfter);
                Assert.Equal(openBefore, openAfter);
            }
            finally
            {
                // Whatever failed above, the request is not left open on the demo: the approver cancels it.
                if (request.Length == 36 && Iam53State("PayProfileChangeRequests", request) is "0" or "1")
                    await Iam53PostAsync(approver, $"{Iam53Changes}/Cancel/{request}", $"{Iam53Changes}/Details/{request}", new() { ["reason"] = Iam53CancelReason });
            }
        }

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_IAM_53_vps_a_grade_change_is_requested_and_rejected_and_a_grade_in_use_is_not_deleted()
    {
        var recorder = StartIam53("reference-change",
            "Demo server: the processor requests a higher maximum for a grade (pending; the grade as it was), the approver rejects it with a note; the delete request of a grade pay profiles name is refused (409); the grade is as it was");
        var password = DemoPassword();
        var (processor, _) = await SignInAsync(Iam53Processor, password, "/ui/payroll/");
        var (approver, _) = await SignInAsync(Iam53Approver, password, "/ui/payroll/");
        using (processor)
        using (approver)
        {
            var grade = Sql("payroll_db",
                "select g.\"Id\" || '|' || g.\"SalaryScaleId\" || '|' || g.\"GradeCode\" || '|' || g.\"GradeNameAr\" || '|' || g.\"BaseSalary\" || '|' || g.\"MinSalary\" || '|' || g.\"MaxSalary\" " +
                "from \"PayGrades\" g where g.\"MaxSalary\" > 0 and exists (select 1 from \"EmployeePayProfiles\" p where p.\"PayGradeId\" = g.\"Id\") " +
                "and not exists (select 1 from \"PayrollReferenceChangeRequests\" r where r.\"EntityId\" = g.\"Id\" and r.\"State\" = 0) " +
                "order by g.\"GradeCode\" desc limit 1").SingleOrDefault()?.Split('|');
            AcceptanceGate.RequireData(recorder, grade is { Length: 7 }, "لا توجد درجة عليها ملفات أجر", "The demo has no grade that pay profiles name.");
            var id = grade![0];
            var rowBefore = Iam53Row("PayGrades", id);
            var openBefore = Iam53Open();
            var requestsBefore = Sql("payroll_db", $"select count(*) from \"PayrollReferenceChangeRequests\" where \"EntityId\" = '{id}'").Single();

            var edit = $"/ui/payroll/PayGrades/Edit/{id}";
            var (submitted, submittedPath, _) = await Iam53PostAsync(processor, edit, edit, new()
            {
                ["SalaryScaleId"] = grade[1],
                ["GradeCode"] = grade[2],
                ["GradeNameAr"] = grade[3],
                ["BaseSalary"] = Iam53Money(grade[4], 0),
                ["MinSalary"] = Iam53Money(grade[5], 0),
                ["MaxSalary"] = Iam53Money(grade[6], 1000),
                ["Reason"] = Iam53GradeReason,
                ["LegalReference"] = Iam53Legal
            });
            var request = Regex.Match(submittedPath, "[0-9a-fA-F-]{36}").Value;
            try
            {
                Assert.True(submitted == 200 && submittedPath.Contains("/PayrollReferenceChanges/Details/", StringComparison.OrdinalIgnoreCase),
                    $"The grade's edit form answered {submitted} at {submittedPath}.");
                var details = $"{Iam53ReferenceChanges}/Details/{request}";
                var pending = Iam53State("PayrollReferenceChangeRequests", request);
                var rowWhilePending = Iam53Row("PayGrades", id);
                var (ownApprove, _, _) = await Iam53PostAsync(processor, $"{Iam53ReferenceChanges}/Approve/{request}", details, new() { ["note"] = Iam53Marker });
                var (rejected, _, _) = await Iam53PostAsync(approver, $"{Iam53ReferenceChanges}/Reject/{request}", details, new() { ["note"] = Iam53RejectNote });
                var closed = Iam53State("PayrollReferenceChangeRequests", request);

                var delete = $"/ui/payroll/PayGrades/Delete/{id}";
                var requestsBeforeDelete = Sql("payroll_db", $"select count(*) from \"PayrollReferenceChangeRequests\" where \"EntityId\" = '{id}'").Single();
                var (deleted, _, deletedBody) = await Iam53PostAsync(processor, delete, delete, new() { ["Reason"] = "حذف درجة لم تعد مستعملة", ["LegalReference"] = Iam53Legal });
                var requestsAfterDelete = Sql("payroll_db", $"select count(*) from \"PayrollReferenceChangeRequests\" where \"EntityId\" = '{id}'").Single();
                var rowAfter = Iam53Row("PayGrades", id);
                var openAfter = Iam53Open();
                recorder.WriteScenarioDbChecks(new
                {
                    grade = id,
                    code = grade[2],
                    request,
                    submitted,
                    pending,
                    ownApprove,
                    rejected,
                    closed,
                    deleted,
                    deleteSaysWhy = deletedBody.Contains("لا تُحذف الدرجة", StringComparison.Ordinal),
                    requestsBefore,
                    requestsBeforeDelete,
                    requestsAfterDelete,
                    rowBefore,
                    rowWhilePending,
                    rowAfter,
                    openBefore,
                    openAfter
                });

                Assert.Equal("0", pending);
                Assert.Equal(rowBefore, rowWhilePending);
                Assert.Equal(403, ownApprove);
                Assert.Equal(200, rejected);
                Assert.Equal("2", closed);
                Assert.Equal(409, deleted);
                Assert.Contains("لا تُحذف الدرجة", deletedBody, StringComparison.Ordinal);
                Assert.Equal(requestsBeforeDelete, requestsAfterDelete);
                Assert.Equal(rowBefore, rowAfter);
                Assert.Equal(openBefore, openAfter);
            }
            finally
            {
                // Whatever failed above, the request is not left open on the demo: the approver rejects it.
                if (request.Length == 36 && Iam53State("PayrollReferenceChangeRequests", request) == "0")
                    await Iam53PostAsync(approver, $"{Iam53ReferenceChanges}/Reject/{request}", $"{Iam53ReferenceChanges}/Details/{request}", new() { ["note"] = Iam53RejectNote });
            }
        }

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_IAM_53_vps_the_salary_and_reference_change_screens_open_in_arabic_under_one_policy_with_no_violation()
    {
        var recorder = StartIam53("screens",
            "Demo server: the salary change and reference change screens, signed in as e2e-payroll-processor in a real Chromium: one CSP policy each, no securitypolicyviolation, Arabic and right-to-left, users named by the registry's span and no user id printed");
        var request = Sql("payroll_db",
            $"select \"Id\" || '|' || \"EmployeeId\" || '|' || \"RequestedBy\" from \"PayProfileChangeRequests\" where \"SourceReference\" = '{Iam53Marker}' order by \"RequestedAt\" desc limit 1")
            .SingleOrDefault()?.Split('|');
        var reference = Sql("payroll_db",
            "select \"Id\" || '|' || \"EntityId\" from \"PayrollReferenceChangeRequests\" where \"EntityType\" = 1 and \"EntityId\" is not null order by \"RequestedAt\" desc limit 1")
            .SingleOrDefault()?.Split('|');
        AcceptanceGate.RequireData(recorder, request is { Length: 3 } && reference is { Length: 2 },
            "لا يوجد طلب تغيير راتب أو طلب بيانات مرجعية من فحوص الخادم", "Run the salary change and the reference change checks first: this check opens the requests they leave closed.");
        Ux067KeyPage[] pages =
        [
            new("salary-changes", Iam53Changes),
            new("salary-change-details", $"{Iam53Changes}/Details/{request![0]}"),
            new("salary-change-form", $"/ui/payroll/EmployeePayProfiles/Create?employeeId={request[1]}"),
            new("pay-profiles-outside-bounds", "/ui/payroll/EmployeePayProfiles?outsideBounds=true"),
            new("reference-changes", Iam53ReferenceChanges),
            new("reference-change-details", $"{Iam53ReferenceChanges}/Details/{reference![0]}"),
            new("grade-details", $"/ui/payroll/PayGrades/Details/{reference[1]}"),
            new("grade-edit", $"/ui/payroll/PayGrades/Edit/{reference[1]}"),
        ];

        var password = DemoPassword();
        var output = Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan53", "vps", "screens");
        IReadOnlyList<Ux067PageResult>? results = null;
        var attempts = 0;
        var networkChanges = new List<string>();
        // A page load fails with ERR_NETWORK_CHANGED when a network interface of the machine that runs the check changes
        // under Chromium (a local container restarting does it). That says nothing of the server: the probe is run again.
        while (results is null && attempts < 5)
        {
            attempts++;
            var playwright = await Playwright.CreateAsync();
            var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var state = Path.Combine(Path.GetTempPath(), $"iam53-state-{Guid.NewGuid():N}.json");
            try
            {
                await Ux067PageProbe.SignInAndSaveStateAsync(browser, $"https://{AppDomain}", Iam53Processor, password, state);
                results = await Ux067PageProbe.ProbeAsync(browser, $"https://{AppDomain}", state, pages, [(1366, 768), (390, 844)], Path.Combine(output, "screenshots"));
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("ERR_NETWORK_CHANGED", StringComparison.Ordinal))
            {
                networkChanges.Add(ex.Message.Split('\n')[0]);
            }
            finally
            {
                await browser.DisposeAsync();
                playwright.Dispose();
                if (File.Exists(state)) File.Delete(state);
            }
        }

        Assert.True(results is not null, $"The probe met a network change on this machine {attempts} times: {string.Join(" / ", networkChanges)}");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "csp-pages.json"), Ux067PageProbe.ToJson(results!));
        recorder.WriteScenarioSteps(string.Join("\n", results!.Select(r =>
            $"- {r.Name}: HTTP {r.Status}, {r.CspHeaders} CSP header(s), {r.CspPolicies} policies, {r.Violations.Count} violation(s)" + (r.Note is null ? "" : $" ({r.Note})"))));

        // Arabic, right-to-left, and users by name: the request's page as the server sends it to the processor.
        var (processor, _) = await SignInAsync(Iam53Processor, password, "/ui/payroll/");
        string details;
        using (processor)
        using (var page = await processor.GetAsync($"https://{AppDomain}{Iam53Changes}/Details/{request[0]}"))
            details = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        var visible = Regex.Replace(Regex.Replace(details, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase), "<[^>]+>", " ");
        var failures = results!.Where(r => !r.Passes || r.Status != 200)
            .Select(r => $"{r.Name}: HTTP {r.Status}, {r.CspPolicies} policies, {r.Violations.Count} violations {string.Join("; ", r.Violations.Select(v => $"{v.Directive} {v.BlockedUri} {v.Source}"))} {r.Note}")
            .ToList();
        recorder.WriteScenarioDbChecks(new
        {
            attempts,
            networkChanges,
            pages = results!.Select(r => new { r.Name, r.Status, r.CspHeaders, r.CspPolicies, violations = r.Violations.Count, r.Passes }),
            rightToLeft = details.Contains("dir=\"rtl\"", StringComparison.Ordinal),
            arabic = details.Contains("lang=\"ar\"", StringComparison.Ordinal),
            requesterNamedByTheRegistry = details.Contains($"data-hr-user-id=\"{request[2]}\"", StringComparison.OrdinalIgnoreCase),
            requesterIdPrinted = visible.Contains(request[2], StringComparison.OrdinalIgnoreCase)
        });

        Assert.Equal(pages.Length, results!.Count);
        Assert.True(failures.Count == 0, "Screens failing the CSP check:\n" + string.Join("\n", failures));
        Assert.Contains("dir=\"rtl\"", details, StringComparison.Ordinal);
        Assert.Contains("lang=\"ar\"", details, StringComparison.Ordinal);
        Assert.Contains($"data-hr-user-id=\"{request[2]}\"", details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(request[2], visible, StringComparison.OrdinalIgnoreCase);
        recorder.WriteResult("Pass");
    }
}
