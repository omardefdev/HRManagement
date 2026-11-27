using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-PAY-55a–e (PLAN-55) on the prod-like stack, through the gateway. An employee's bank account is registered by one person and
/// verified by another against the bank's reference; a run pins the account at its snapshot, so a change of account afterwards does
/// not move that run's line; retiring a pinned account before disbursement holds the employee in that run; a salary change's new pay
/// profile version keeps the unit and the regime; and a run's readiness report lists the employees with no account.
/// <para>
/// The scenarios run in the order of their names and build on each other: 55a leaves two agency-A employees with an account in force
/// (found again by the reference of their verification), 55c calculates the Regular runs of May and June 2029 (months the seed's v1
/// framework governs, that no other class takes and that the interim gate never reaches), 55d and 55e read May's run. A second run of
/// the class on the same stack stops at 55c's precondition: the two months are taken, and a rerun needs a fresh stack. 55b works in
/// agency B, whose current month no class freezes (AC-IAM-53a freezes agency A's). Nothing stops or recreates a container.
/// </para>
/// The branch parts of the plan (the branch manager's pay profile list; an employee outside the user's branch) are proved by
/// <c>Pay055BankAccountHttpTests</c> with a restricted principal: no persona holds payroll and a branch (D-IAM-3).
/// </summary>
[RequiredAcceptance]
[TestCaseOrderer(ScenarioNameOrderer.TypeName, ScenarioNameOrderer.AssemblyName)]
public class AcPay55BankAccountTests
{
    private const string Ac = "AC-PAY-55";
    private const string Processor = "payroll-processor";
    private const string Approver = "payroll-approver";
    private const string Admin = "a-directory-checker";             // hr-admin: holds both payroll policies (refused both since PLAN-58)
    private const string ForeignProcessor = "b-payroll-processor";
    private const string ForeignApprover = "b-payroll-approver";
    private const string Payroll = "payroll_db";
    private const string Changes = "api/payroll/pay-profile-changes";
    private const string FirstReference = "كتاب المصرف التجاري 55/أ-1";
    private const string SecondReference = "كتاب المصرف التجاري 55/أ-2";
    private const string ReplacementReference = "كتاب المصرف التجاري 55/ج-3";
    private const string BankName = "المصرف التجاري السوري";
    private const int PendingStatus = 0;
    private const int ActiveStatus = 1;
    private const int RetiredStatus = 3;

    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly string TenantB = AcceptanceSeedIds.AgencyBId.ToString("D");
    private static readonly DateOnly May = new(2029, 5, 1);
    private static readonly DateOnly June = new(2029, 6, 1);

    private static Task<string> Db(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(Payroll, sql);

    private static Task<long> Count(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(Payroll, sql);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Accounts(string employee) => $"api/payroll/employees/{employee}/bank-accounts";

    private static string Readiness(string run) => $"api/payroll/runs/{run}/bank-readiness";

    /// <summary>The first day of the current month on the service's clock (Asia/Damascus).</summary>
    private static DateOnly MonthStart
    {
        get
        {
            var today = HRManagement.ServiceDefaults.Localization.HrLocalTime.ToLocal(
                DateTime.UtcNow, HRManagement.ServiceDefaults.Localization.HrLocalTime.DefaultZone);
            return new DateOnly(today.Year, today.Month, 1);
        }
    }

    // ---------------------------------------------------------------- test IBANs

    /// <summary>
    /// A test IBAN for an employee: «SY», two check digits computed here (ISO 7064 mod 97-10, written independently of the product's
    /// validator), and a BBAN made of the scenario's number and the employee's id. Never a real account.
    /// </summary>
    private static string Iban(string employee, int variant)
    {
        var bban = (variant.ToString("D2", CultureInfo.InvariantCulture) + "55" + employee.Replace("-", "", StringComparison.Ordinal)).ToUpperInvariant()[..24];
        var check = 98 - Mod97(bban + "SY00");
        return "SY" + check.ToString("D2", CultureInfo.InvariantCulture) + bban;
    }

    private static int Mod97(string text)
    {
        var remainder = 0;
        foreach (var c in text)
        {
            remainder = char.IsDigit(c)
                ? ((remainder * 10) + (c - '0')) % 97
                : ((remainder * 100) + (c - 'A' + 10)) % 97;
        }

        return remainder;
    }

    /// <summary>The same number with its last check digit changed: it fails the mod 97 check.</summary>
    private static string WithWrongCheckDigit(string iban) => iban[..3] + (iban[3] == '9' ? '8' : (char)(iban[3] + 1)) + iban[4..];

    private static string Masked(string number) => new string('•', number.Length - 4) + number[^4..];

    // ---------------------------------------------------------------- the API, as one persona

    private sealed class Api(HttpClient http, EvidenceRecorder recorder, string persona, string tag) : IDisposable
    {
        public static async Task<Api> OpenAsync(EvidenceRecorder recorder, string persona, string tag) =>
            new(await AcceptanceHarness.CreatePayrollClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D")),
                recorder, persona, tag);

        public async Task<(HttpStatusCode Status, JsonElement Body, string Text)> SendAsync(string step, HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            JsonElement json = default;
            if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
                json = JsonSerializer.Deserialize<JsonElement>(text);
            recorder.CaptureHttp($"{tag}-{step}-{persona}", method.Method, "/" + path, (int)response.StatusCode,
                json.ValueKind == JsonValueKind.Undefined ? (text.Length > 300 ? text[..300] : text) : json);
            return (response.StatusCode, json, text);
        }

        public Task<(HttpStatusCode Status, JsonElement Body, string Text)> PostAsync(string step, string path, object? body = null) =>
            SendAsync(step, HttpMethod.Post, path, body ?? new { });

        public Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetAsync(string step, string path) =>
            SendAsync(step, HttpMethod.Get, path);

        public void Dispose() => http.Dispose();
    }

    private static string Text(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static object Registration(string number) =>
        new { accountKind = "Iban", accountNumber = number, bankNameAr = BankName, holderNameAr = (string?)null };

    // ---------------------------------------------------------------- payroll_db

    /// <summary>The employee's accounts as «status:id» in the order they were registered.</summary>
    private static Task<string> AccountsOfAsync(string employee) =>
        Db("SELECT COALESCE(string_agg(\"Status\"::text || ':' || \"Id\"::text, ',' ORDER BY \"CreatedAt\"), '') " +
           $"FROM \"EmployeeBankAccounts\" WHERE \"EmployeeId\"='{employee}';");

    private static Task<long> AccountCountAsync(string employee) =>
        Count($"SELECT count(*) FROM \"EmployeeBankAccounts\" WHERE \"EmployeeId\"='{employee}';");

    private static Task<string> ActiveAccountAsync(string employee) =>
        Db($"SELECT COALESCE(max(\"Id\"::text), '') FROM \"EmployeeBankAccounts\" WHERE \"EmployeeId\"='{employee}' AND \"Status\"={ActiveStatus};");

    private static Task<long> StatusAsync(string account) => Count($"SELECT \"Status\" FROM \"EmployeeBankAccounts\" WHERE \"Id\"='{account}';");

    /// <summary>
    /// Agency-A employees no other scenario uses: an active pay profile with a unit, no bank account, no salary change request, and
    /// not the two employees the seed gives a branch. The lowest employee ids: AC-IAM-53 picks from the highest.
    /// </summary>
    private static async Task<string[]> PickEmployeesAsync(EvidenceRecorder recorder, string step, int count)
    {
        var seeded = string.Join(",", new[] { AcceptanceSeedIds.EmpBr1Id, AcceptanceSeedIds.EmpBr2Id }.Select(id => $"'{id:D}'"));
        var rows = await Db(
            "SELECT COALESCE(string_agg(e, ',' ORDER BY e), '') FROM (SELECT p.\"EmployeeId\"::text AS e FROM \"EmployeePayProfiles\" p " +
            $"WHERE p.\"TenantId\"='{TenantA}' AND p.\"IsActive\" AND p.\"OrgUnitId\" IS NOT NULL AND p.\"EmployeeId\" NOT IN ({seeded}) " +
            "AND NOT EXISTS (SELECT 1 FROM \"EmployeeBankAccounts\" a WHERE a.\"EmployeeId\" = p.\"EmployeeId\") " +
            "AND NOT EXISTS (SELECT 1 FROM \"PayProfileChangeRequests\" r WHERE r.\"EmployeeId\" = p.\"EmployeeId\") " +
            $"ORDER BY p.\"EmployeeId\" LIMIT {count}) picked;");
        var picked = rows.Split(',', StringSplitOptions.RemoveEmptyEntries);
        AcceptanceGate.RequireData(recorder, picked.Length == count,
            $"{step}: لا يوجد في الجهة A {count} موظفين بملف أجر فعّال وبلا حساب مصرفي وبلا طلب تغيير راتب",
            $"{step}: agency A has no {count} employees with an active pay profile, no bank account and no salary change request.");
        return picked;
    }

    /// <summary>The employee 55a left with an account verified under <paramref name="reference"/> (the latest, on a stack used twice).</summary>
    private static async Task<string> EmployeeVerifiedUnderAsync(EvidenceRecorder recorder, string step, string reference)
    {
        var employee = await Db(
            $"SELECT COALESCE((SELECT \"EmployeeId\"::text FROM \"EmployeeBankAccounts\" WHERE \"TenantId\"='{TenantA}' " +
            $"AND \"VerificationReference\"='{reference}' ORDER BY \"VerifiedAt\" DESC LIMIT 1), '');");
        AcceptanceGate.RequireData(recorder, Guid.TryParse(employee, out _),
            $"{step}: لا يوجد حساب تحقق منه 55a بالمرجع «{reference}»: يُشغَّل الصنف كاملاً بترتيبه",
            $"{step}: no account verified by 55a under the reference '{reference}': the class runs whole, in the order of its scenarios.");
        return employee;
    }

    /// <summary>The Regular run of agency A whose period starts on <paramref name="month"/>, or empty.</summary>
    private static Task<string> RunOfAsync(DateOnly month) =>
        Db($"SELECT COALESCE((SELECT \"Id\"::text FROM \"PayrollRuns\" WHERE \"TenantId\"='{TenantA}' AND \"RunType\"='Regular' " +
           $"AND \"PeriodStart\"::date = '{Day(month)}' AND \"Status\" NOT IN (5, 8) ORDER BY \"CreatedAt\" DESC LIMIT 1), '');");

    private static Task<string> PinnedOnLineAsync(string run, string employee) =>
        Db($"SELECT COALESCE((SELECT COALESCE(\"BankAccountId\"::text, 'none') FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{run}' AND \"EmployeeId\"='{employee}'), 'no-line');");

    private static Task<string> PinnedOnSnapshotAsync(string run, string employee) =>
        Db("SELECT COALESCE((SELECT COALESCE(sn.\"BankAccountId\"::text, 'none') FROM \"PayrollRunInputSnapshots\" sn " +
           $"JOIN \"PayrollRunJobs\" j ON j.\"Id\" = sn.\"PayrollRunJobId\" WHERE j.\"PayrollRunId\"='{run}' AND sn.\"EmployeeId\"='{employee}' " +
           "ORDER BY j.\"CreatedAt\" DESC LIMIT 1), 'no-snapshot');");

    /// <summary>The active holds of the employee in the run whose reason starts with ACCOUNT-RETIRED.</summary>
    private static Task<long> RetiredHoldsAsync(string run, string employee) =>
        Count($"SELECT count(*) FROM \"PaymentHolds\" WHERE \"PayrollRunId\"='{run}' AND \"EmployeeId\"='{employee}' " +
              "AND \"Status\"='Active' AND \"HoldType\"='Manual' AND \"Reason\" LIKE 'ACCOUNT-RETIRED:%';");

    /// <summary>audit.json: payroll_db's audit rows of the records the scenario touched (the action, the user and the time; no values).</summary>
    private static async Task AuditAsync(RecruitmentSession s, params string[] entityIds)
    {
        var ids = string.Join(",", entityIds.Where(id => Guid.TryParse(id, out _)).Select(id => $"'{id}'"));
        var rows = ids.Length == 0 ? "" : await Db(
            "SELECT COALESCE(string_agg(\"EntityType\" || '|' || \"EntityId\" || '|' || \"Action\" || '|' || COALESCE(\"UserId\"::text,'') || '|' || " +
            $"to_char(\"CreatedAt\", 'YYYY-MM-DD\"T\"HH24:MI:SS'), E'\\n' ORDER BY \"CreatedAt\"), '') FROM \"AuditLogs\" WHERE \"EntityId\" IN ({ids});");
        s.Recorder.WriteAudit(new
        {
            scenario = s.Key,
            explicitAuditQuery = true,
            source = "payroll_db.AuditLogs",
            entityIds,
            rows = rows.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        });
    }

    /// <summary>Creates and calculates the Regular run of a month of agency A, as the processor. Returns the run's id.</summary>
    private static async Task<string> CalculateMonthAsync(Api processor, string step, DateOnly month)
    {
        // A class that ran earlier on this stack may have added an employment record after the seed (AC-IAM-50b does), and the
        // service-regime gate then refuses every run of the agency. The seed's classification is applied to such records first.
        await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(TenantA);
        var end = month.AddMonths(1).AddDays(-1);
        var (created, createdBody, _) = await processor.PostAsync($"{step}-create-run", "api/payroll/runs", new
        {
            periodStart = month.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            periodEnd = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            runDate = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        });
        Assert.True(created is HttpStatusCode.OK or HttpStatusCode.Created, $"{step}: creating the run of {Day(month)[..7]} answered {(int)created}: {Text(createdBody, "error")}");
        var run = createdBody.GetProperty("id").GetGuid().ToString("D");
        var (calculated, calculatedBody, _) = await processor.PostAsync($"{step}-calculate", $"api/payroll/runs/{run}/calculate");
        Assert.True(calculated == HttpStatusCode.OK, $"{step}: calculating the run of {Day(month)[..7]} answered {(int)calculated}: {Text(calculatedBody, "error")}");
        return run;
    }

    private static string[] Ids(JsonElement body, string property) =>
        body.GetProperty(property).EnumerateArray().Select(e => e.GetGuid().ToString("D")).ToArray();

    // ---------------------------------------------------------------- 55a

    [SkippableFact]
    public async Task Ac55a_an_account_is_registered_by_one_person_and_verified_by_another_with_the_banks_reference()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "55a",
            "AC-PAY-55a: the processor registers a valid IBAN for an employee and it waits for verification; the approver verifies it with the bank's reference and it is in force. Negative: the person who registered an account cannot verify it (400), an IBAN with a wrong check digit is refused (400) and no row is written, and a second waiting account of the same employee is refused (409).",
            Processor, Approver, Admin);
        s.Steps("""
            1. Two agency-A employees with an active pay profile and no bank account are picked (payroll_db). before.json: neither has
               an account.
            2. payroll-processor registers a test IBAN (check digits computed by the test) for the first employee, typed with spaces
               and in lower case → 201, PendingVerification. The stored number is normalised; the list shows it masked to its last
               four characters, to the processor and on the employee's page, and no answer carries the whole number.
            3. Negative: payroll-processor asks to verify his own registration → refused: he does not hold the approval policy (403).
            4. Negative: an IBAN whose check digit is wrong, for the second employee → 400 with an Arabic message, and no row.
            5. Negative: a second registration for the first employee while one waits → 409; he still has one account.
            6. payroll-approver verifies the first employee's account with the reference of the bank's confirmation → Active, verified
               by another person than its registrar.
            7. Negative, the plan's own: a person who holds both payroll policies (hr-admin) cannot register and then verify his own
               account. Since PLAN-58 (D-PAY-20) a token that carries both conflicting roles passes neither act: his registration for
               the second employee → 403, the conflict named, and no row. (That the registrar never verifies his own registration with
               another token is proved by Pay055BankAccountHttpTests, one person with two tokens.) payroll-processor registers it;
               without a reference the approver's verification is 400. Then payroll-approver verifies it with a reference → Active.
            8. after.json: each employee has one account in force; payroll_db's audit rows of both accounts name no account number.
            """);

        var picked = await PickEmployeesAsync(s.Recorder, "55a", 2);
        var (first, second) = (picked[0], picked[1]);
        var (firstIban, secondIban) = (Iban(first, 1), Iban(second, 2));
        s.Recorder.WriteBefore(new
        {
            scenario = "55a",
            explicitDomainSnapshot = true,
            employees = picked,
            accounts = new[] { await AccountsOfAsync(first), await AccountsOfAsync(second) }
        });

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "55a");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "55a");
        using var admin = await Api.OpenAsync(s.Recorder, Admin, "55a");

        // 2. The registration.
        var typed = string.Join(' ', firstIban.ToLowerInvariant().Chunk(4).Select(c => new string(c)));
        var (registered, registeredBody, registeredText) = await processor.PostAsync("register", Accounts(first), Registration(typed));
        Assert.True(registered == HttpStatusCode.Created, $"55a: the registration answered {(int)registered}: {Text(registeredBody, "error")}");
        Assert.Equal("PendingVerification", Text(registeredBody, "status"));
        var firstAccount = registeredBody.GetProperty("accountId").GetGuid().ToString("D");
        var stored = await Db($"SELECT \"AccountNumber\" || '|' || \"Status\" || '|' || (\"CreatedBy\" IS NOT NULL)::text FROM \"EmployeeBankAccounts\" WHERE \"Id\"='{firstAccount}';");
        var (listed, _, listText) = await processor.GetAsync("list", Accounts(first));
        var page = await s[Processor].GetAsync("page", $"ui/payroll/EmployeeBankAccounts?employeeId={first}");

        // 3. The processor does not hold the approval policy.
        var (ownVerify, _, _) = await processor.PostAsync("verify-own", $"{Accounts(first)}/{firstAccount}/verify", new { verificationReference = FirstReference });
        var afterOwnVerify = await StatusAsync(firstAccount);

        // 4. A wrong check digit.
        var wrong = WithWrongCheckDigit(secondIban);
        var (bad, badBody, badText) = await processor.PostAsync("register-bad-check-digit", Accounts(second), Registration(wrong));
        var rowsAfterBad = await AccountCountAsync(second);

        // 5. A second waiting account.
        var (again, againBody, _) = await processor.PostAsync("register-second-pending", Accounts(first), Registration(Iban(first, 9)));
        var rowsAfterSecond = await AccountCountAsync(first);

        // 6. Another person verifies.
        var (verified, verifiedBody, _) = await approver.PostAsync("verify", $"{Accounts(first)}/{firstAccount}/verify", new { verificationReference = FirstReference });
        Assert.True(verified == HttpStatusCode.OK, $"55a: the verification answered {(int)verified}: {Text(verifiedBody, "error")}");
        var twoPeople = await Db($"SELECT \"Status\" || '|' || (\"VerifiedBy\" IS NOT NULL AND \"VerifiedBy\" <> \"CreatedBy\")::text || '|' || \"VerificationReference\" FROM \"EmployeeBankAccounts\" WHERE \"Id\"='{firstAccount}';");

        // 7. The person who holds both policies registers nothing (PLAN-58, D-PAY-20): 403, the conflict named, no row.
        var (self, selfBody, _) = await admin.PostAsync("register-by-admin", Accounts(second), Registration(secondIban));
        var rowsAfterAdmin = await AccountCountAsync(second);
        var (processorRegistered, processorBody, _) = await processor.PostAsync("register-second", Accounts(second), Registration(secondIban));
        Assert.True(processorRegistered == HttpStatusCode.Created, $"55a: the second registration answered {(int)processorRegistered}: {Text(processorBody, "error")}");
        var secondAccount = processorBody.GetProperty("accountId").GetGuid().ToString("D");
        var (noReference, noReferenceBody, _) = await approver.PostAsync("verify-without-reference", $"{Accounts(second)}/{secondAccount}/verify", new { verificationReference = "  " });
        var afterNoReference = await StatusAsync(secondAccount);
        var (secondVerified, secondVerifiedBody, _) = await approver.PostAsync("verify-second", $"{Accounts(second)}/{secondAccount}/verify", new { verificationReference = SecondReference });
        Assert.True(secondVerified == HttpStatusCode.OK, $"55a: the second verification answered {(int)secondVerified}: {Text(secondVerifiedBody, "error")}");

        // 8. The audit rows hold no number.
        var auditWithNumber = await Count(
            $"SELECT count(*) FROM \"AuditLogs\" WHERE \"EntityId\" IN ('{firstAccount}','{secondAccount}') " +
            $"AND (COALESCE(\"ChangedFields\",'') LIKE '%{firstIban}%' OR COALESCE(\"ChangedFields\",'') LIKE '%{secondIban}%');");
        var auditRows = await Count($"SELECT count(*) FROM \"AuditLogs\" WHERE \"EntityId\" IN ('{firstAccount}','{secondAccount}');");
        await AuditAsync(s, firstAccount, secondAccount);
        s.Recorder.WriteAfter(new
        {
            scenario = "55a",
            firstEmployee = new { employee = first, accounts = await AccountsOfAsync(first), storedNormalisedPendingWithRegistrar = stored.Replace(firstIban, Masked(firstIban), StringComparison.Ordinal), twoPeopleAndReference = twoPeople },
            secondEmployee = new { employee = second, accounts = await AccountsOfAsync(second) },
            processorVerifiesHisOwn = (int)ownVerify,
            wrongCheckDigit = new { status = (int)bad, error = Text(badBody, "error"), rows = rowsAfterBad },
            secondPending = new { status = (int)again, error = Text(againBody, "error"), rows = rowsAfterSecond },
            holderOfBothPoliciesRegisters = new { status = (int)self, error = Text(selfBody, "error"), rows = rowsAfterAdmin },
            verificationWithoutReference = new { status = (int)noReference, error = Text(noReferenceBody, "error") },
            auditRows,
            auditRowsWithANumber = auditWithNumber
        });

        Assert.Equal($"{firstIban}|{PendingStatus}|true", stored);
        Assert.Equal(HttpStatusCode.OK, listed);
        Assert.DoesNotContain(firstIban, registeredText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(firstIban, listText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Masked(firstIban), listText, StringComparison.Ordinal);
        Assert.DoesNotContain(firstIban, page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Masked(firstIban), page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, ownVerify);
        Assert.Equal(PendingStatus, afterOwnVerify);
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        Assert.Matches(@"\p{IsArabic}", Text(badBody, "error"));
        Assert.DoesNotContain(wrong, badText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, rowsAfterBad);
        Assert.Equal(HttpStatusCode.Conflict, again);
        Assert.Equal(1, rowsAfterSecond);
        Assert.Equal($"{ActiveStatus}|true|{FirstReference}", twoPeople);
        Assert.Equal(HttpStatusCode.Forbidden, self);
        Assert.Contains(HrManagementRoles.PayrollApprover, Text(selfBody, "error"), StringComparison.Ordinal);
        Assert.Equal(0, rowsAfterAdmin);
        Assert.Equal(HttpStatusCode.BadRequest, noReference);
        Assert.Equal(PendingStatus, afterNoReference);
        Assert.Equal(ActiveStatus, await StatusAsync(secondAccount));
        Assert.Equal(firstAccount, await ActiveAccountAsync(first));
        Assert.Equal(secondAccount, await ActiveAccountAsync(second));
        Assert.True(auditRows >= 4, $"55a: {auditRows} audit rows for two registrations and two verifications.");
        Assert.Equal(0, auditWithNumber);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 55b

    [SkippableFact]
    public async Task Ac55b_a_salary_changes_new_pay_profile_version_keeps_the_unit_and_the_regime()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "55b",
            "AC-PAY-55b: an employee's salary changes by PLAN-53's request and approval; the new active pay profile version carries the organisational unit and the legal regime of the version it replaces, and the employee is still in the pay profile list.",
            ForeignProcessor, ForeignApprover);
        s.Steps("""
            1. An agency-B employee is picked: an active pay profile that began before this month, with a unit and a grade that leaves
               room, and no salary change request. Agency B, because AC-IAM-53a freezes agency A's current month and the classes of a
               group run in an order that changes; no class creates a run in agency B's current month. before.json: no Regular run of
               agency B covers the current month; the profile's unit and regime.
               Test-only precondition (psql, payroll_db): the seed's pay profiles have no regime code, so the copy would be proved on
               an empty value; the employee's profile gets one.
            2. b-payroll-processor requests a salary inside the grade's bounds from the first day of the current month, with a reason
               and a legal reference → PendingApproval. b-payroll-approver approves → Applied.
            3. The previous version is closed the day before; the new version is the only active one, and its unit and regime equal
               the previous version's. The pay profile list still shows the employee. (That a branch manager still lists him is proved
               by Pay055BankAccountHttpTests with a restricted principal: no persona holds payroll and a branch.)
            """);

        var month = MonthStart;
        var regular = await Db(
            $"SELECT COALESCE(string_agg(r.\"Status\"::text, ','), '') FROM \"PayrollRuns\" r WHERE r.\"TenantId\"='{TenantB}' AND r.\"RunType\"='Regular' " +
            $"AND r.\"PeriodStart\" <= '{Day(month.AddMonths(1).AddDays(-1))}' AND r.\"PeriodEnd\" >= '{Day(month)}' AND r.\"Status\" NOT IN (5, 8);");
        AcceptanceGate.RequireData(s.Recorder, regular.Length == 0,
            $"للجهة B تشغيل رواتب اعتيادي يغطي الشهر الجاري ({Day(month)[..7]}): السيناريو يحتاج مكدساً جديداً",
            $"Agency B already has a Regular payroll run covering {Day(month)[..7]} ({regular}): start a fresh stack.");
        var row = await Db(
            "SELECT COALESCE((SELECT p.\"EmployeeId\"::text || '|' || p.\"Id\"::text || '|' || p.\"BaseSalary\"::text || '|' || g.\"MinSalary\"::text || '|' || g.\"MaxSalary\"::text " +
            "FROM \"EmployeePayProfiles\" p JOIN \"PayGrades\" g ON g.\"Id\" = p.\"PayGradeId\" " +
            $"WHERE p.\"TenantId\"='{TenantB}' AND p.\"IsActive\" AND p.\"OrgUnitId\" IS NOT NULL AND p.\"EffectiveFrom\" < '{Day(month)}' " +
            "AND g.\"MaxSalary\" > 0 AND p.\"BaseSalary\" BETWEEN g.\"MinSalary\" AND g.\"MaxSalary\" " +
            "AND (p.\"BaseSalary\" + 1000 <= g.\"MaxSalary\" OR p.\"BaseSalary\" - 1000 >= g.\"MinSalary\") " +
            "AND NOT EXISTS (SELECT 1 FROM \"PayProfileChangeRequests\" r WHERE r.\"EmployeeId\" = p.\"EmployeeId\") " +
            "ORDER BY p.\"EmployeeId\" LIMIT 1), '');");
        var parts = row.Split('|');
        AcceptanceGate.RequireData(s.Recorder, parts.Length == 5,
            "55b: لا يوجد موظف في الجهة B بملف أجر فعّال ودرجة تسمح بالتغيير وبلا طلب سابق",
            "55b: agency B has no employee with an active pay profile, a grade with room and no earlier request.");
        var (employee, profile) = (parts[0], parts[1]);
        var salary = decimal.Parse(parts[2], CultureInfo.InvariantCulture);
        var raised = salary + 1000m <= decimal.Parse(parts[4], CultureInfo.InvariantCulture) ? salary + 1000m : salary - 1000m;

        await Db($"UPDATE \"EmployeePayProfiles\" SET \"RegimeCode\"='law50' WHERE \"Id\"='{profile}'; SELECT 1;");
        var carried = "\"OrgUnitId\"::text || '|' || COALESCE(\"RegimeCode\",'∅')";
        var carriedBefore = await Db($"SELECT {carried} FROM \"EmployeePayProfiles\" WHERE \"Id\"='{profile}';");
        s.Recorder.WriteBefore(new
        {
            scenario = "55b",
            explicitDomainSnapshot = true,
            agency = "AGENCY-B",
            month = Day(month),
            regularRunsOfTheMonth = regular,
            employee,
            profile,
            carriedFields = "OrgUnitId|RegimeCode",
            carriedBefore,
            carriedFieldsSetByTheTest = "RegimeCode"
        });

        using var processor = await Api.OpenAsync(s.Recorder, ForeignProcessor, "55b");
        using var approver = await Api.OpenAsync(s.Recorder, ForeignApprover, "55b");
        var (submitted, submittedBody, _) = await processor.PostAsync("submit", Changes, new
        {
            employeeId = employee,
            proposedBaseSalary = raised,
            proposedPayGradeId = (Guid?)null,
            effectiveFrom = Day(month),
            reason = "تعديل الراتب بقرار إداري",
            legalReference = "القرار 55 لعام 2026"
        });
        Assert.True(submitted == HttpStatusCode.Created, $"55b: the request was not accepted ({(int)submitted}): {Text(submittedBody, "error")}");
        var request = submittedBody.GetProperty("requestId").GetGuid().ToString("D");
        var (approved, approvedBody, _) = await approver.PostAsync("approve", $"{Changes}/{request}/approve", new { note = "موافق" });
        Assert.True(approved == HttpStatusCode.OK, $"55b: the approval answered {(int)approved}: {Text(approvedBody, "error")}");

        var active = await Db(
            "SELECT count(*)::text || '|' || COALESCE(max(\"Id\"::text), '') || '|' || COALESCE(max(\"EffectiveFrom\"::date::text), '') " +
            $"FROM \"EmployeePayProfiles\" WHERE \"EmployeeId\"='{employee}' AND \"IsActive\";");
        var newProfile = active.Split('|')[1];
        var carriedAfter = await Db($"SELECT {carried} FROM \"EmployeePayProfiles\" WHERE \"Id\"='{newProfile}';");
        var previous = await Db($"SELECT \"IsActive\"::text || '|' || COALESCE(\"EffectiveTo\"::date::text, '') FROM \"EmployeePayProfiles\" WHERE \"Id\"='{profile}';");
        var list = await s[ForeignProcessor].GetAsync("pay-profiles", $"ui/payroll/EmployeePayProfiles?employeeId={employee}&isActive=true");
        await AuditAsync(s, request, profile, newProfile);
        s.Recorder.WriteAfter(new
        {
            scenario = "55b",
            state = Text(approvedBody, "state"),
            active,
            previous,
            carriedBefore,
            carriedAfter,
            carriedUnchanged = carriedBefore == carriedAfter,
            listedInThePayProfileList = list.Contains(newProfile, StringComparison.OrdinalIgnoreCase) || list.Contains(employee, StringComparison.OrdinalIgnoreCase)
        });

        Assert.Equal("Applied", Text(approvedBody, "state"));
        Assert.StartsWith("1|", active, StringComparison.Ordinal);
        Assert.NotEqual(profile, newProfile);
        Assert.EndsWith($"|{Day(month)}", active, StringComparison.Ordinal);
        Assert.Equal($"false|{Day(month.AddDays(-1))}", previous);
        Assert.DoesNotContain("∅", carriedBefore, StringComparison.Ordinal);
        Assert.Equal(carriedBefore, carriedAfter);
        Assert.Contains(employee, list, StringComparison.OrdinalIgnoreCase);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 55c

    [SkippableFact]
    public async Task Ac55c_a_run_pins_the_account_at_its_snapshot_and_a_replacement_verified_afterwards_applies_from_the_next_month()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "55c",
            "AC-PAY-55c: a run is calculated and the employee's line carries his bank account, counted by the readiness report. Negative: a replacement account verified after the calculation does not move the run's line (and the retired account's line is behind a hold); the next month's run takes the new account.",
            Processor, Approver);
        s.Steps("""
            1. Precondition, in before.json: agency A has no run that covers May or June 2029 (two months the seed's v1 framework
               governs, that no other class takes), and no pending adjustment with an amount (isolation: a regular run takes every
               free approved adjustment of its agency, so one left by another class would enter this scenario's runs and move the
               lines it reads). A second run of the class on the same stack stops here: a rerun needs a fresh stack.
               The two employees 55a left with an account in force are found by the references of their verification.
            2. payroll-processor creates and calculates the Regular run of May 2029. The first employee's input snapshot and salary
               line carry his account in force; the readiness report counts the lines with an account as payroll_db does, and does
               not list him among the employees without one.
            3. Negative: payroll-processor registers a replacement account for him and payroll-approver verifies it. The first account
               is retired and the replacement is in force; May's line and snapshot still carry the first account; the employee has an
               active ACCOUNT-RETIRED hold in May's run; the readiness report names him among the lines whose account was retired.
            4. payroll-processor creates and calculates the Regular run of June 2029: his line carries the replacement.
            """);

        var covering = await Db(
            $"SELECT COALESCE(string_agg(r.\"RunType\"::text || ':' || r.\"Status\"::text, ','), '') FROM \"PayrollRuns\" r WHERE r.\"TenantId\"='{TenantA}' " +
            $"AND r.\"PeriodStart\" <= '{Day(June.AddMonths(1).AddDays(-1))}' AND r.\"PeriodEnd\" >= '{Day(May)}' AND r.\"Status\" NOT IN (5, 8);");
        AcceptanceGate.RequireData(s.Recorder, covering.Length == 0,
            "للجهة A تشغيل رواتب يغطي أيار أو حزيران 2029: السيناريو يحتاج مكدساً جديداً",
            $"Agency A already has a payroll run covering May or June 2029 ({covering}): AC-PAY-55c took the months in an earlier run; start a fresh stack.");
        var pendingAmounts = await Count($"SELECT count(*) FROM \"PayrollAdjustments\" WHERE \"TenantId\"='{TenantA}' AND \"PayrollRunId\" IS NULL AND \"Amount\" <> 0;");
        AcceptanceGate.RequireData(s.Recorder, pendingAmounts == 0,
            "للجهة A تسوية معلّقة بمبلغ: تدخل تشغيلات هذا السيناريو وتغيّر سطوره (عزل الأصناف)",
            $"Agency A has {pendingAmounts} pending adjustment(s) with an amount: a regular run would take them into this scenario's runs (isolation between classes).");
        var employee = await EmployeeVerifiedUnderAsync(s.Recorder, "55c", FirstReference);
        var other = await EmployeeVerifiedUnderAsync(s.Recorder, "55c", SecondReference);
        var firstAccount = await ActiveAccountAsync(employee);
        var otherAccount = await ActiveAccountAsync(other);
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(firstAccount, out _) && Guid.TryParse(otherAccount, out _),
            "55c: أحد موظفَي 55a بلا حساب نافذ", "55c: one of 55a's two employees has no account in force.");
        s.Recorder.WriteBefore(new
        {
            scenario = "55c",
            explicitDomainSnapshot = true,
            runsCoveringMayOrJune2029 = covering,
            pendingAdjustmentsWithAnAmount = pendingAmounts,
            employee,
            accountInForce = firstAccount,
            accounts = await AccountsOfAsync(employee)
        });

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "55c");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "55c");

        // 2. May is calculated: the snapshot pins the account in force, the line copies it.
        var may = await CalculateMonthAsync(processor, "may", May);
        var lineAfterMay = await PinnedOnLineAsync(may, employee);
        var snapshotAfterMay = await PinnedOnSnapshotAsync(may, employee);
        var (readinessStatus, readiness, _) = await processor.GetAsync("readiness-may", Readiness(may));
        Assert.True(readinessStatus == HttpStatusCode.OK, $"55c: the readiness report answered {(int)readinessStatus}.");
        var lines = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{may}';");
        var linesWithAccount = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{may}' AND \"BankAccountId\" IS NOT NULL;");
        var netBefore = await Db($"SELECT \"NetPay\"::text FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{may}' AND \"EmployeeId\"='{employee}';");

        // 3. A replacement, registered and verified after the calculation.
        var replacementIban = Iban(employee, 3);
        var (registered, registeredBody, _) = await processor.PostAsync("register-replacement", Accounts(employee), Registration(replacementIban));
        Assert.True(registered == HttpStatusCode.Created, $"55c: the replacement's registration answered {(int)registered}: {Text(registeredBody, "error")}");
        var replacement = registeredBody.GetProperty("accountId").GetGuid().ToString("D");
        var inForceWhileItWaits = await ActiveAccountAsync(employee);
        var (verified, verifiedBody, _) = await approver.PostAsync("verify-replacement", $"{Accounts(employee)}/{replacement}/verify", new { verificationReference = ReplacementReference });
        Assert.True(verified == HttpStatusCode.OK, $"55c: the replacement's verification answered {(int)verified}: {Text(verifiedBody, "error")}");
        var lineAfterReplacement = await PinnedOnLineAsync(may, employee);
        var snapshotAfterReplacement = await PinnedOnSnapshotAsync(may, employee);
        var netAfter = await Db($"SELECT \"NetPay\"::text FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{may}' AND \"EmployeeId\"='{employee}';");
        var holds = await RetiredHoldsAsync(may, employee);
        var holdReason = await Db($"SELECT COALESCE(max(\"Reason\"), '') FROM \"PaymentHolds\" WHERE \"PayrollRunId\"='{may}' AND \"EmployeeId\"='{employee}';");
        var (_, readinessLater, _) = await processor.GetAsync("readiness-may-after-replacement", Readiness(may));

        // 4. June takes the replacement.
        var june = await CalculateMonthAsync(processor, "june", June);
        var lineInJune = await PinnedOnLineAsync(june, employee);

        await AuditAsync(s, firstAccount, replacement);
        s.Recorder.WriteAfter(new
        {
            scenario = "55c",
            may,
            june,
            firstAccount,
            replacement,
            lineAfterMay,
            snapshotAfterMay,
            lineAfterReplacement,
            snapshotAfterReplacement,
            lineInJune,
            netUnchanged = netBefore == netAfter,
            accounts = await AccountsOfAsync(employee),
            readiness = new
            {
                lines = readiness.GetProperty("lines").GetInt32(),
                linesWithAccount = readiness.GetProperty("linesWithAccount").GetInt32(),
                linesWithoutAccount = readiness.GetProperty("linesWithoutAccount").GetInt32(),
                payrollDbLines = lines,
                payrollDbLinesWithAccount = linesWithAccount
            },
            retiredHoldsOfTheEmployeeInMay = holds,
            holdNamesNoNumber = !holdReason.Contains("SY", StringComparison.Ordinal)
        });

        Assert.Equal(firstAccount, lineAfterMay);
        Assert.Equal(firstAccount, snapshotAfterMay);
        Assert.Equal(lines, readiness.GetProperty("lines").GetInt32());
        Assert.Equal(linesWithAccount, readiness.GetProperty("linesWithAccount").GetInt32());
        Assert.Equal(lines - linesWithAccount, readiness.GetProperty("linesWithoutAccount").GetInt32());
        Assert.True(linesWithAccount >= 2, $"55c: {linesWithAccount} lines of May carry an account; 55a's two employees have one.");
        Assert.DoesNotContain(employee, Ids(readiness, "employeesWithoutAccount"));
        Assert.Equal(otherAccount, await PinnedOnLineAsync(may, other));
        // The account in force stayed in force while its replacement waited.
        Assert.Equal(firstAccount, inForceWhileItWaits);
        Assert.Equal("Active", Text(verifiedBody, "status"));
        Assert.Equal(RetiredStatus, await StatusAsync(firstAccount));
        Assert.Equal(replacement, await ActiveAccountAsync(employee));
        // The run's line did not move, in its account or in its amount.
        Assert.Equal(firstAccount, lineAfterReplacement);
        Assert.Equal(firstAccount, snapshotAfterReplacement);
        Assert.Equal(netBefore, netAfter);
        Assert.Equal(1, holds);
        Assert.StartsWith("ACCOUNT-RETIRED:", holdReason, StringComparison.Ordinal);
        Assert.DoesNotContain(replacementIban, holdReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(employee, Ids(readinessLater, "employeesWithRetiredAccount"));
        // The next cycle takes the new account.
        Assert.Equal(replacement, lineInJune);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 55d

    [SkippableFact]
    public async Task Ac55d_retiring_a_pinned_account_before_disbursement_holds_the_employee_in_that_run()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "55d",
            "AC-PAY-55d (negative): the account a run pinned is retired before the run is disbursed; the employee gets an ACCOUNT-RETIRED payment hold in that run, so no money goes to an account that was withdrawn.",
            Approver, Processor);
        s.Steps("""
            1. The second employee of 55a has an account in force that May 2029's run (55c) pinned on his line; no payment order of the
               line has left for the channel. before.json: the account, the line, no hold of his in the run.
            2. Negative: payroll-processor asks to retire it → 403 (retiring takes the approval policy); payroll-approver asks without a
               reason → 400. The account is still in force and no hold exists.
            3. payroll-approver retires it with a reason → Retired. The employee has an active payment hold in May's run, of type
               Manual, whose reason starts with ACCOUNT-RETIRED and names no number; the line still carries the retired account; the
               employee has no account in force.
            """);

        var employee = await EmployeeVerifiedUnderAsync(s.Recorder, "55d", SecondReference);
        var may = await RunOfAsync(May);
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(may, out _),
            "55d: لا يوجد تشغيل أيار 2029 الذي يحسبه 55c: يُشغَّل الصنف كاملاً بترتيبه",
            "55d: May 2029's run, which 55c calculates, does not exist: the class runs whole, in the order of its scenarios.");
        var account = await ActiveAccountAsync(employee);
        var pinned = await PinnedOnLineAsync(may, employee);
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(account, out _) && pinned == account,
            "55d: حساب الموظف النافذ ليس المثبَّت في سطره من تشغيل أيار",
            $"55d: the employee's account in force ({account}) is not the one pinned on his line of May's run ({pinned}).");
        var disbursed = await Count(
            "SELECT count(*) FROM \"PaymentOrders\" o JOIN \"SalaryLines\" l ON l.\"Id\" = o.\"SalaryLineId\" " +
            $"WHERE l.\"PayrollRunId\"='{may}' AND l.\"EmployeeId\"='{employee}' AND o.\"Status\" IN ('Submitted', 'Acknowledged', 'Settled', 'PartiallySettled');");
        s.Recorder.WriteBefore(new
        {
            scenario = "55d",
            explicitDomainSnapshot = true,
            employee,
            run = may,
            account,
            pinnedOnTheLine = pinned,
            paymentOrdersThatLeftForTheChannel = disbursed,
            retiredHolds = await RetiredHoldsAsync(may, employee)
        });
        Assert.Equal(0, disbursed);

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "55d");
        using var approver = await Api.OpenAsync(s.Recorder, Approver, "55d");
        var (byProcessor, _, _) = await processor.PostAsync("retire-by-processor", $"{Accounts(employee)}/{account}/retire", new { reason = "أغلق الموظف حسابه" });
        var (noReason, noReasonBody, _) = await approver.PostAsync("retire-without-reason", $"{Accounts(employee)}/{account}/retire", new { reason = "" });
        var statusAfterRefusals = await StatusAsync(account);
        var holdsAfterRefusals = await RetiredHoldsAsync(may, employee);

        var (retired, retiredBody, _) = await approver.PostAsync("retire", $"{Accounts(employee)}/{account}/retire", new { reason = "أغلق الموظف حسابه لدى المصرف" });
        Assert.True(retired == HttpStatusCode.OK, $"55d: the retirement answered {(int)retired}: {Text(retiredBody, "error")}");
        var holds = await RetiredHoldsAsync(may, employee);
        var hold = await Db(
            "SELECT COALESCE(max(\"Id\"::text || '|' || \"HoldType\" || '|' || \"Status\" || '|' || \"Reason\"), '') FROM \"PaymentHolds\" " +
            $"WHERE \"PayrollRunId\"='{may}' AND \"EmployeeId\"='{employee}';");
        var holdsPage = await s[Approver].GetAsync("holds", "ui/payroll/PaymentHolds");
        await AuditAsync(s, account, hold.Split('|')[0]);
        s.Recorder.WriteAfter(new
        {
            scenario = "55d",
            retireByTheProcessor = (int)byProcessor,
            retireWithoutAReason = new { status = (int)noReason, error = Text(noReasonBody, "error") },
            status = Text(retiredBody, "status"),
            holdsPlaced = retiredBody.GetProperty("holdsPlaced").GetInt32(),
            retiredHoldsOfTheEmployeeInTheRun = holds,
            hold = hold.Split('|').Skip(1).ToArray(),
            pinnedOnTheLine = await PinnedOnLineAsync(may, employee),
            accountInForce = await ActiveAccountAsync(employee)
        });

        Assert.Equal(HttpStatusCode.Forbidden, byProcessor);
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        Assert.Matches(@"\p{IsArabic}", Text(noReasonBody, "error"));
        Assert.Equal(ActiveStatus, statusAfterRefusals);
        Assert.Equal(0, holdsAfterRefusals);
        Assert.Equal("Retired", Text(retiredBody, "status"));
        Assert.True(retiredBody.GetProperty("holdsPlaced").GetInt32() >= 1);
        Assert.Equal(RetiredStatus, await StatusAsync(account));
        Assert.Equal(1, holds);
        var parts = hold.Split('|');
        Assert.Equal(("Manual", "Active"), (parts[1], parts[2]));
        Assert.StartsWith("ACCOUNT-RETIRED:", parts[3], StringComparison.Ordinal);
        Assert.Contains("أغلق الموظف حسابه لدى المصرف", hold, StringComparison.Ordinal);
        Assert.DoesNotContain(Iban(employee, 2), hold, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(account, await PinnedOnLineAsync(may, employee));
        Assert.Equal("", await ActiveAccountAsync(employee));
        Assert.Contains("ACCOUNT-RETIRED", holdsPage, StringComparison.Ordinal);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 55e

    [SkippableFact]
    public async Task Ac55e_an_employee_without_an_account_is_in_the_readiness_report_and_another_agency_gets_404()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "55e",
            "AC-PAY-55e: an employee with no bank account is listed in the run's readiness report, by id on the API and by name on the run's page. Negative: another agency's processor asks for an agency-A employee's bank accounts and gets 404.",
            Processor, ForeignProcessor);
        s.Steps("""
            1. May 2029's run (55c) holds lines of employees with no bank account: they blocked neither its calculation nor its report.
               before.json: the run's counts in payroll_db.
            2. payroll-processor reads the readiness report on the API: the lines without an account are counted as in payroll_db and
               an employee without one is listed by his id.
            3. payroll-processor opens the run's page: the «جاهزية الحسابات المصرفية» card shows the same counts, and lists that
               employee with a link to his bank accounts and no id as text.
            4. Negative: b-payroll-processor (agency B) asks for the accounts of the agency-A employee of 55a, and tries to register one
               for him → 404 both times; nothing of the account is in the answer, and the employee's accounts are as they were.
            """);

        var may = await RunOfAsync(May);
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(may, out _),
            "55e: لا يوجد تشغيل أيار 2029 الذي يحسبه 55c: يُشغَّل الصنف كاملاً بترتيبه",
            "55e: May 2029's run, which 55c calculates, does not exist: the class runs whole, in the order of its scenarios.");
        var lines = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{may}';");
        var without = await Count($"SELECT count(*) FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{may}' AND \"BankAccountId\" IS NULL;");
        var someone = await Db($"SELECT COALESCE(min(\"EmployeeId\"::text), '') FROM \"SalaryLines\" WHERE \"PayrollRunId\"='{may}' AND \"BankAccountId\" IS NULL;");
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(someone, out _),
            "55e: كل سطور تشغيل أيار تحمل حساباً", "55e: every line of May's run carries an account.");
        var owner = await EmployeeVerifiedUnderAsync(s.Recorder, "55e", FirstReference);
        var ownerAccountsBefore = await AccountsOfAsync(owner);
        s.Recorder.WriteBefore(new { scenario = "55e", explicitDomainSnapshot = true, run = may, lines, linesWithoutAccount = without, anEmployeeWithoutAccount = someone, ownerAccounts = ownerAccountsBefore });

        using var processor = await Api.OpenAsync(s.Recorder, Processor, "55e");
        using var foreign = await Api.OpenAsync(s.Recorder, ForeignProcessor, "55e");

        // 2. By id, on the API.
        var (status, readiness, _) = await processor.GetAsync("readiness", Readiness(may));
        Assert.True(status == HttpStatusCode.OK, $"55e: the readiness report answered {(int)status}.");
        var listedIds = Ids(readiness, "employeesWithoutAccount");

        // 3. By name, on the run's page.
        var page = await s[Processor].GetAsync("run-page", $"ui/payroll/PayrollRuns/Details/{may}");
        var card = Regex.Match(page, @"data-testid=""run-bank-readiness"".*?بنود الرواتب", RegexOptions.Singleline);
        var withoutOnPage = Regex.Match(card.Value, @"data-testid=""run-bank-readiness-without""[^>]*>\s*(\d+)");
        var withOnPage = Regex.Match(card.Value, @"data-testid=""run-bank-readiness-with""[^>]*>\s*(\d+)");
        var item = Regex.Match(card.Value, @"<li>(?:(?!</li>).)*?employeeId=" + Regex.Escape(someone) + @"(?:(?!</li>).)*?</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        // What a reader sees in that list item: the tags (and their attributes) removed. The name is written by the server when the
        // directory answered; otherwise the page carries the hook the browser fills the name into (display-names.js).
        var itemText = Regex.Replace(item.Value, "<[^>]+>", " ");
        var nameHook = item.Value.Contains($"data-hr-employee-id=\"{someone}\"", StringComparison.OrdinalIgnoreCase);
        var shownName = itemText.Replace("الحسابات المصرفية", "", StringComparison.Ordinal).Replace("غير متاح", "", StringComparison.Ordinal).Trim();

        // 4. Another agency.
        var (foreignList, _, foreignListText) = await foreign.GetAsync("foreign-list", Accounts(owner));
        var (foreignRegister, _, foreignRegisterText) = await foreign.PostAsync("foreign-register", Accounts(owner), Registration(Iban(owner, 7)));
        var ownerAccountsAfter = await AccountsOfAsync(owner);

        s.Recorder.WriteAfter(new
        {
            scenario = "55e",
            api = new
            {
                lines = readiness.GetProperty("lines").GetInt32(),
                linesWithoutAccount = readiness.GetProperty("linesWithoutAccount").GetInt32(),
                listsTheEmployeeById = listedIds.Contains(someone)
            },
            page = new
            {
                cardFound = card.Success,
                linesWithAccount = withOnPage.Success ? withOnPage.Groups[1].Value : "",
                linesWithoutAccount = withoutOnPage.Success ? withoutOnPage.Groups[1].Value : "",
                listsTheEmployee = item.Success,
                nameWrittenByTheServer = shownName.Length > 0,
                nameFilledByTheBrowserHook = nameHook,
                itemShowsNoId = !Regex.IsMatch(itemText, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-")
            },
            anotherAgency = new { list = (int)foreignList, register = (int)foreignRegister, ownerAccountsUnchanged = ownerAccountsBefore == ownerAccountsAfter }
        });

        Assert.Equal(lines, readiness.GetProperty("lines").GetInt32());
        Assert.Equal(without, readiness.GetProperty("linesWithoutAccount").GetInt32());
        Assert.Equal(without, listedIds.Length);
        Assert.Contains(someone, listedIds);
        Assert.True(card.Success, "55e: the run's page has no bank readiness card.");
        Assert.Equal(without.ToString(CultureInfo.InvariantCulture), withoutOnPage.Groups[1].Value);
        Assert.Equal((lines - without).ToString(CultureInfo.InvariantCulture), withOnPage.Groups[1].Value);
        Assert.True(item.Success, "55e: the readiness card does not list the employee without an account.");
        Assert.DoesNotMatch("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-", itemText);
        Assert.True(shownName.Length > 0 || nameHook, "55e: the list item shows no name and has no hook for one.");
        Assert.Equal(HttpStatusCode.NotFound, foreignList);
        Assert.Equal(HttpStatusCode.NotFound, foreignRegister);
        Assert.DoesNotContain("accountId", foreignListText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accountId\":\"", foreignRegisterText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ownerAccountsBefore, ownerAccountsAfter);
        s.Recorder.WriteResult("Pass");
    }
}
