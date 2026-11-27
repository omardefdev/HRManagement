using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using Microsoft.Playwright;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-55 (P8, P-15, P-16) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers, as three
/// people: <c>e2e-payroll-processor</c> registers, <c>e2e-payroll-approver</c> verifies, rejects and retires, and the administrator
/// (<c>hradmin</c>, or the user named in <c>PAY055_VPS_ADMIN</c>), who holds both policies, shows that a registrar never verifies his
/// own registration. The password is read into memory only; evidence goes under <c>artifacts/plan55/vps</c> and is never committed.
/// <para>
/// Nothing these checks make stays usable: every account they register ends <c>Rejected</c> or <c>Retired</c>, whatever fails on the
/// way. Their numbers are test IBANs whose check digits are computed here, never a real account; the marker of the checks is inside
/// the number (<c>CHK55</c> after the check digits), which no reader sees whole. No run, payment order, hold or pay profile of the demo
/// changes: they are compared before and after by checksums (and with P0's picture when <c>PAY055_VPS_PROFILES_MD5</c> and
/// <c>PAY055_VPS_PROFILES_AMOUNTS_MD5</c> carry its values).
/// </para>
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Pay55Processor = "e2e-payroll-processor";
    private const string Pay55Approver = "e2e-payroll-approver";
    private const string Pay55Api = "/ui/payroll/api/payroll";
    private const string Pay55Pages = "/ui/payroll/EmployeeBankAccounts";
    /// <summary>The marker of every account these checks register: the start of its number's BBAN.</summary>
    private const string Pay55Marker = "CHK55";
    private const string Pay55Mine = "\"AccountNumber\" like 'SY__" + Pay55Marker + "%'";
    private const string Pay55Bank = "المصرف التجاري السوري - فرع المزة";
    private const string Pay55Reference = "كتاب المصرف التجاري السوري رقم 4471/ص تاريخ 2026-11-20";
    private const string Pay55RetireReason = "أغلق الموظف حسابه لدى المصرف وطلب تحويل راتبه إلى حساب آخر";
    private const string Pay55RejectReason = "رقم الحساب لا يطابق كتاب تأكيد المصرف المرفق بالطلب";
    private const string Pay55SelfVerification = "لا يتحقق من الحساب مَن سجّله";
    private const string Pay55BadCheckDigits = "خانتا التحقق لا تطابقان الرقم";

    private static string Pay55Admin => Environment.GetEnvironmentVariable("PAY055_VPS_ADMIN") is { Length: > 0 } user ? user : "hradmin";

    private static EvidenceRecorder StartPay55(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"pay55-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan55", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    /// <summary>A test IBAN for an employee: «SY», check digits computed here (ISO 7064 mod 97-10), then the marker. Never a real account.</summary>
    private static string Pay55Iban(string employee, int variant)
    {
        var bban = (Pay55Marker + variant.ToString("D2", CultureInfo.InvariantCulture) + employee.Replace("-", "", StringComparison.Ordinal)).ToUpperInvariant()[..24];
        var check = 98 - Pay55Mod97(bban + "SY00");
        return "SY" + check.ToString("D2", CultureInfo.InvariantCulture) + bban;
    }

    private static int Pay55Mod97(string text)
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

    private static string Pay55Masked(string number) => new string('•', number.Length - 4) + number[^4..];

    /// <summary>P0's two checksums of the active pay profiles (the same expressions as the picture): with the unit and the regime, and without.</summary>
    private static string Pay55Profiles() =>
        Sql("payroll_db",
            "select count(*) || '~' || " +
            "md5(string_agg(md5(\"Id\"::text||'|'||coalesce(\"OrgUnitId\"::text,'')||'|'||coalesce(\"RegimeCode\",'')||'|'||\"BaseSalary\"::text||'|'||coalesce(\"PayGradeId\"::text,'')), '' order by \"Id\")) || '~' || " +
            "md5(string_agg(md5(\"Id\"::text||'|'||\"BaseSalary\"::text||'|'||coalesce(\"PayGradeId\"::text,'')), '' order by \"Id\")) " +
            "from \"EmployeePayProfiles\" where \"IsActive\"").Single();

    /// <summary>What no check may move: the runs, the lines and snapshots that pin an account, the payment orders and the holds.</summary>
    private static string Pay55PayData() =>
        Sql("payroll_db",
            "select (select count(*) || ':' || md5(coalesce(string_agg(t::text, ';' order by t.\"Id\"), '')) from \"PayrollRuns\" t) || '~' || " +
            "(select count(*) || ':' || count(\"BankAccountId\") from \"SalaryLines\") || '~' || " +
            "(select count(*) || ':' || count(\"BankAccountId\") from \"PayrollRunInputSnapshots\") || '~' || " +
            "(select count(*) || ':' || md5(coalesce(string_agg(t.\"Id\"::text || t.\"Status\"::text, ';' order by t.\"Id\"), '')) from \"PaymentOrders\" t) || '~' || " +
            "(select count(*) || ':' || md5(coalesce(string_agg(t.\"Id\"::text || t.\"Status\"::text, ';' order by t.\"Id\"), '')) from \"PaymentHolds\" t)").Single();

    /// <summary>An account's place: status, has a registrar, decided by another person, the reference, the rejection and retirement reasons.</summary>
    private static string Pay55Account(string id) =>
        Sql("payroll_db",
            "select \"Status\" || '~' || (\"CreatedBy\" is not null)::text || '~' || " +
            "coalesce((coalesce(\"VerifiedBy\", \"RejectedBy\", \"RetiredBy\") <> \"CreatedBy\")::text, '') || '~' || coalesce(\"VerificationReference\", '') || '~' || " +
            "coalesce(\"RejectionReason\", '') || '~' || coalesce(\"RetirementReason\", '') " +
            $"from \"EmployeeBankAccounts\" where \"Id\" = '{id}'").SingleOrDefault() ?? "";

    private static string Pay55Count(string where) => Sql("payroll_db", $"select count(*) from \"EmployeeBankAccounts\" where {where}").Single();

    /// <summary>A demo employee with an active pay profile and no account in force or waiting; <paramref name="skip"/> picks a different one per check.</summary>
    private static string? Pay55Employee(int skip) =>
        Sql("payroll_db",
            "select p.\"EmployeeId\" from \"EmployeePayProfiles\" p where p.\"IsActive\" and not exists " +
            "(select 1 from \"EmployeeBankAccounts\" a where a.\"EmployeeId\" = p.\"EmployeeId\" and a.\"Status\" in (0, 1)) " +
            $"order by p.\"EmployeeId\" limit 1 offset {skip}").SingleOrDefault();

    /// <summary>A signed-in session that sends the antiforgery token of the bank accounts API (the cookie stays in the client).</summary>
    private static async Task<HttpClient> Pay55ApiAsync(string user, string password)
    {
        var (client, _) = await SignInAsync(user, password, "/ui/payroll/");
        using var issued = await client.GetAsync($"https://{AppDomain}{Pay55Api}/bank-accounts/antiforgery");
        var body = await issued.Content.ReadAsStringAsync();
        Assert.True(issued.IsSuccessStatusCode, $"The antiforgery token of the bank accounts API answered {(int)issued.StatusCode} to {user}.");
        using var token = JsonDocument.Parse(body);
        client.DefaultRequestHeaders.Add(token.RootElement.GetProperty("headerName").GetString()!, token.RootElement.GetProperty("requestToken").GetString());
        return client;
    }

    private static async Task<(int Status, string Error, string Body)> Pay55PostAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsync($"https://{AppDomain}{path}",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();
        var error = "";
        if (text.TrimStart().StartsWith('{'))
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.TryGetProperty("error", out var value) && value.ValueKind == JsonValueKind.String)
                error = value.GetString() ?? "";
        }

        return ((int)response.StatusCode, error, text);
    }

    private static string Pay55Accounts(string employee) => $"{Pay55Api}/employees/{employee}/bank-accounts";

    private static object Pay55Registration(string number) => new { accountKind = "Iban", accountNumber = number, bankNameAr = Pay55Bank };

    private static string Pay55Id(string body) => Regex.Match(body, "\"accountId\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"").Groups[1].Value;

    /// <summary>Leaves nothing open: an account of the checks that still waits is rejected, one in force is retired.</summary>
    private static async Task Pay55CloseAsync(HttpClient approver)
    {
        foreach (var row in Sql("payroll_db", $"select \"EmployeeId\" || '~' || \"Id\" || '~' || \"Status\" from \"EmployeeBankAccounts\" where {Pay55Mine} and \"Status\" in (0, 1)"))
        {
            var parts = row.Split('~');
            if (parts[2] == "0")
                await Pay55PostAsync(approver, $"{Pay55Accounts(parts[0])}/{parts[1]}/reject", new { reason = Pay55RejectReason });
            else
                await Pay55PostAsync(approver, $"{Pay55Accounts(parts[0])}/{parts[1]}/retire", new { reason = Pay55RetireReason });
        }
    }

    [SkippableFact]
    public void AC_PAY_55_vps_the_migration_ran_no_account_was_moved_and_the_pay_profiles_kept_their_values()
    {
        var recorder = StartPay55("migration",
            "Demo server: PAY055 is recorded; the accounts table is under forced row security and the pay profiles kept theirs; the demo had no number, so no account was moved; the profile's three old bank columns are still there; no line or snapshot pins an account; no account is in force or waiting; the active profiles' checksums are those of P0's picture");
        var migration = Sql("payroll_db", "select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" like '%PAY055_EmployeeBankAccounts'").Single();
        var rowSecurity = Sql("payroll_db",
            "select string_agg(relname || ':' || relrowsecurity::text || ':' || relforcerowsecurity::text, ',' order by relname) from pg_class " +
            "where relname in ('EmployeeBankAccounts', 'EmployeePayProfiles') and relkind = 'r'").Single();
        var index = Sql("payroll_db",
            "select count(*) from pg_indexes where indexname = 'IX_EmployeeBankAccounts_OneActiveOnePending' and indexdef like 'CREATE UNIQUE INDEX%' and indexdef like '%WHERE%'").Single();
        var columns = Sql("payroll_db",
            "select (select count(*) from information_schema.columns where table_name = 'EmployeePayProfiles' and column_name in ('BankAccountIban', 'BankName', 'BankAccountVerifiedAt')) || '|' || " +
            "(select count(*) from information_schema.columns where column_name = 'BankAccountId' and table_name in ('SalaryLines', 'PayrollRunInputSnapshots'))").Single();
        var moved = Pay55Count("\"CreatedBy\" is null");
        var usable = Pay55Count("\"Status\" in (0, 1)");
        var notTheChecks = Pay55Count($"not ({Pay55Mine})");
        var pinned = Sql("payroll_db",
            "select (select count(\"BankAccountId\") from \"SalaryLines\") || '|' || (select count(\"BankAccountId\") from \"PayrollRunInputSnapshots\")").Single();
        var profiles = Pay55Profiles().Split('~');
        var pictureProfiles = Environment.GetEnvironmentVariable("PAY055_VPS_PROFILES_MD5");
        var pictureAmounts = Environment.GetEnvironmentVariable("PAY055_VPS_PROFILES_AMOUNTS_MD5");

        recorder.WriteScenarioDbChecks(new
        {
            migration,
            rowSecurity,
            index,
            columns,
            columnsFormat = "old bank columns still on the profile | new BankAccountId columns",
            accountsWithNoRegistrar = moved,
            accountsInForceOrWaiting = usable,
            accountsThatAreNotTheChecks = notTheChecks,
            pinned,
            pinnedFormat = "lines with an account | snapshots with an account",
            activeProfiles = profiles[0],
            profilesChecksum = profiles[1],
            amountsChecksum = profiles[2],
            comparedWithThePicture = pictureProfiles is not null || pictureAmounts is not null
        });

        Assert.Equal("1", migration);
        Assert.Equal("EmployeeBankAccounts:true:true,EmployeePayProfiles:true:true", rowSecurity);
        Assert.Equal("1", index);
        Assert.Equal("3|2", columns);
        Assert.Equal("0", moved);                                                      // the demo had no number: nothing was moved
        Assert.Equal("0", usable);                                                     // no account is verified, or waits, on the demo
        Assert.Equal("0", notTheChecks);
        Assert.Equal("0|0", pinned);
        // The salary and the grade of every active profile, as in the picture; the whole row too, but for what the repair filled
        // (PAY055_VPS_PROFILES_MD5 is given only when the repair's report says it filled nothing).
        if (pictureAmounts is not null) Assert.Equal(pictureAmounts, profiles[2]);
        if (pictureProfiles is not null) Assert.Equal(pictureProfiles, profiles[1]);
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_55_vps_an_account_is_registered_by_one_person_verified_by_another_and_then_retired()
    {
        var recorder = StartPay55("account",
            "Demo server: the processor registers a test IBAN for a demo employee (pending, masked in every answer); his own verification is refused and changes nothing; a number with a wrong check digit answers 400 and writes no row; the approver verifies it with the bank's reference (in force) and then retires it with a reason; no run pinned it, no hold was placed, and no run, order, hold or pay profile of the demo changed");
        var password = DemoPassword();
        using var processor = await Pay55ApiAsync(Pay55Processor, password);
        using var approver = await Pay55ApiAsync(Pay55Approver, password);
        var employee = Pay55Employee(0);
        AcceptanceGate.RequireData(recorder, employee is not null, "لا يوجد موظف بملف أجر فعّال على خادم العرض", "The demo has no employee with an active pay profile and no account.");
        var number = Pay55Iban(employee!, 1);
        var payDataBefore = Pay55PayData();
        var profilesBefore = Pay55Profiles();
        try
        {
            // A wrong check digit first: 400, the Arabic reason, no row.
            var rowsBefore = Pay55Count("true");
            var wrong = number[..3] + (number[3] == '9' ? '8' : (char)(number[3] + 1)) + number[4..];
            var (bad, badError, badBody) = await Pay55PostAsync(processor, Pay55Accounts(employee!), Pay55Registration(wrong));
            var rowsAfterBad = Pay55Count("true");

            // Typed as a person types it: lower case, in groups of four.
            var typed = string.Join(' ', number.ToLowerInvariant().Chunk(4).Select(c => new string(c)));
            var (registered, registeredError, registeredBody) = await Pay55PostAsync(processor, Pay55Accounts(employee!), Pay55Registration(typed));
            var id = Pay55Id(registeredBody);
            Assert.True(registered == 201 && id.Length == 36, $"The registration answered {registered}: {registeredError}");
            var pending = Pay55Account(id);
            var stored = Sql("payroll_db", $"select (\"AccountNumber\" = '{number}')::text from \"EmployeeBankAccounts\" where \"Id\" = '{id}'").Single();
            using var listed = await processor.GetAsync($"https://{AppDomain}{Pay55Accounts(employee!)}");
            var list = await listed.Content.ReadAsStringAsync();

            var (own, ownError, _) = await Pay55PostAsync(processor, $"{Pay55Accounts(employee!)}/{id}/verify", new { verificationReference = Pay55Reference });
            var afterOwn = Pay55Account(id);
            var (noReference, noReferenceError, _) = await Pay55PostAsync(approver, $"{Pay55Accounts(employee!)}/{id}/verify", new { verificationReference = " " });
            var afterNoReference = Pay55Account(id);
            var (verified, verifiedError, _) = await Pay55PostAsync(approver, $"{Pay55Accounts(employee!)}/{id}/verify", new { verificationReference = Pay55Reference });
            var inForce = Pay55Account(id);
            var pinnedWhileInForce = Sql("payroll_db",
                $"select (select count(*) from \"SalaryLines\" where \"BankAccountId\" = '{id}') || '|' || (select count(*) from \"PayrollRunInputSnapshots\" where \"BankAccountId\" = '{id}')").Single();
            var (retired, retiredError, retiredBody) = await Pay55PostAsync(approver, $"{Pay55Accounts(employee!)}/{id}/retire", new { reason = Pay55RetireReason });
            var closed = Pay55Account(id);
            var auditRows = Sql("payroll_db",
                $"select count(*) || '|' || count(*) filter (where coalesce(\"ChangedFields\", '') like '%{number}%') from \"AuditLogs\" where \"EntityId\" = '{id}'").Single();
            var payDataAfter = Pay55PayData();
            var profilesAfter = Pay55Profiles();

            recorder.WriteScenarioDbChecks(new
            {
                employee,
                account = id,
                masked = Pay55Masked(number),
                bad,
                badError,
                noRowAfterTheBadNumber = rowsBefore == rowsAfterBad,
                registered,
                pending,
                storedNormalised = stored,
                accountFormat = "status~has a registrar~decided by another person~reference~rejection reason~retirement reason",
                theListShowsTheMaskedNumberOnly = list.Contains(Pay55Masked(number), StringComparison.Ordinal) && !list.Contains(number, StringComparison.OrdinalIgnoreCase),
                own,
                ownError,
                noReference,
                noReferenceError,
                verified,
                verifiedError,
                inForce,
                pinnedWhileInForce,
                retired,
                retiredError,
                holdsPlaced = Regex.Match(retiredBody, "\"holdsPlaced\"\\s*:\\s*(\\d+)").Groups[1].Value,
                closed,
                auditRows,
                auditRowsFormat = "audit rows of the account | of them carrying the number",
                payDataUnchanged = payDataBefore == payDataAfter,
                profilesUnchanged = profilesBefore == profilesAfter
            });

            Assert.Equal(400, bad);
            Assert.Contains(Pay55BadCheckDigits, badError, StringComparison.Ordinal);
            Assert.DoesNotContain(wrong, badBody, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(rowsBefore, rowsAfterBad);
            Assert.Equal("0~true~~~~", pending);
            Assert.Equal("true", stored);
            Assert.DoesNotContain(number, registeredBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(number, list, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Pay55Masked(number), list, StringComparison.Ordinal);
            // The registrar never verifies his own registration: this user is stopped by the approval policy (403) where he does not
            // hold it, and by the handler's rule (400) where he does. Either way nothing changes.
            Assert.True(own is 400 or 403, $"The registrar's own verification answered {own}: {ownError}");
            Assert.Equal(pending, afterOwn);
            Assert.Equal(400, noReference);
            Assert.Equal(pending, afterNoReference);
            Assert.True(verified == 200, $"The verification answered {verified}: {verifiedError}");
            Assert.Equal($"1~true~true~{Pay55Reference}~~", inForce);
            Assert.Equal("0|0", pinnedWhileInForce);
            Assert.True(retired == 200, $"The retirement answered {retired}: {retiredError}");
            Assert.Contains("\"holdsPlaced\":0", retiredBody.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.Equal($"3~true~true~{Pay55Reference}~~{Pay55RetireReason}", closed);
            var audit = auditRows.Split('|');
            Assert.True(int.Parse(audit[0], CultureInfo.InvariantCulture) >= 3, $"The account's acts are not in the audit log: {auditRows}");   // registered, verified, retired
            Assert.Equal("0", audit[1]);                                                   // and none of its rows carries the number
            Assert.Equal(payDataBefore, payDataAfter);
            Assert.Equal(profilesBefore, profilesAfter);
        }
        finally
        {
            await Pay55CloseAsync(approver);
        }

        Assert.Equal("0", Pay55Count("\"Status\" in (0, 1)"));
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_55_vps_a_second_account_is_not_verified_by_its_registrar_and_the_approver_rejects_it()
    {
        var recorder = StartPay55("second-account",
            "Demo server: the administrator, who holds the processing and the approval policies, registers a test IBAN for another demo employee; his own verification answers 400 with the rule's Arabic reason (the handler's separation of duties, not a policy); a second waiting account for the same employee answers 409; the approver rejects the account with a reason; nothing is left in force or waiting");
        var password = DemoPassword();
        using var admin = await Pay55ApiAsync(Pay55Admin, password);
        using var processor = await Pay55ApiAsync(Pay55Processor, password);
        using var approver = await Pay55ApiAsync(Pay55Approver, password);
        var employee = Pay55Employee(1);
        AcceptanceGate.RequireData(recorder, employee is not null, "لا يوجد موظف ثانٍ بملف أجر فعّال على خادم العرض", "The demo has no second employee with an active pay profile and no account.");
        var number = Pay55Iban(employee!, 2);
        var payDataBefore = Pay55PayData();
        try
        {
            var accountsBefore = Pay55Count($"\"EmployeeId\" = '{employee}'");
            var (registered, registeredError, registeredBody) = await Pay55PostAsync(admin, Pay55Accounts(employee!), Pay55Registration(number));
            // PLAN-58 (D-PAY-20): in demo mode hradmin holds both payroll policies, so his token carries a conflicting pair and passes no
            // financial act: his registration is 403 with the pair named, and nothing is written. The registrar is then the processor,
            // who does not hold the approval policy: his own verification is refused by the policy (403) before the handler's rule
            // «لا يتحقق من الحساب مَن سجّله» (that rule, with one person and two tokens, is proved by Pay055BankAccountHttpTests).
            var adminRefused = registered == 403 && registeredError.Contains("payroll-", StringComparison.Ordinal);
            var accountsAfterAdmin = Pay55Count($"\"EmployeeId\" = '{employee}'");
            var registrar = admin;
            if (adminRefused)
            {
                registrar = processor;
                (registered, registeredError, registeredBody) = await Pay55PostAsync(processor, Pay55Accounts(employee!), Pay55Registration(number));
            }

            var id = Pay55Id(registeredBody);
            Assert.True(registered == 201 && id.Length == 36,
                $"The registration answered {registered}: {registeredError}. If it is 403 the user does not hold Payroll.Process on this server: name another in PAY055_VPS_ADMIN.");
            var pending = Pay55Account(id);
            var (own, ownError, _) = await Pay55PostAsync(registrar, $"{Pay55Accounts(employee!)}/{id}/verify", new { verificationReference = Pay55Reference });
            var afterOwn = Pay55Account(id);
            var (again, againError, _) = await Pay55PostAsync(registrar, Pay55Accounts(employee!), Pay55Registration(Pay55Iban(employee!, 3)));
            var waiting = Pay55Count($"\"EmployeeId\" = '{employee}' and \"Status\" = 0");
            var (noReason, _, _) = await Pay55PostAsync(approver, $"{Pay55Accounts(employee!)}/{id}/reject", new { reason = "" });
            var afterNoReason = Pay55Account(id);
            var (rejected, rejectedError, _) = await Pay55PostAsync(approver, $"{Pay55Accounts(employee!)}/{id}/reject", new { reason = Pay55RejectReason });
            var closed = Pay55Account(id);
            var (late, _, _) = await Pay55PostAsync(approver, $"{Pay55Accounts(employee!)}/{id}/verify", new { verificationReference = Pay55Reference });
            var afterLate = Pay55Account(id);
            var payDataAfter = Pay55PayData();

            recorder.WriteScenarioDbChecks(new
            {
                admin = Pay55Admin,
                hradminRefusedByTheConflict = adminRefused,
                accountsBefore,
                accountsAfterAdmin,
                employee,
                account = id,
                registered,
                pending,
                accountFormat = "status~has a registrar~decided by another person~reference~rejection reason~retirement reason",
                own,
                ownError,
                again,
                againError,
                waiting,
                noReason,
                rejected,
                rejectedError,
                closed,
                verifyingARejectedAccount = late,
                payDataUnchanged = payDataBefore == payDataAfter
            });

            Assert.Equal("0~true~~~~", pending);
            if (adminRefused)
            {
                Assert.Equal(accountsBefore, accountsAfterAdmin);
                Assert.Equal(403, own);
            }
            else
            {
                Assert.Equal(400, own);
                Assert.Contains(Pay55SelfVerification, ownError, StringComparison.Ordinal);
            }

            Assert.Equal(pending, afterOwn);
            Assert.Equal(409, again);
            Assert.Equal("1", waiting);
            Assert.Equal(400, noReason);
            Assert.Equal(pending, afterNoReason);
            Assert.True(rejected == 200, $"The rejection answered {rejected}: {rejectedError}");
            Assert.Equal($"2~true~true~~{Pay55RejectReason}~", closed);
            Assert.Equal(409, late);
            Assert.Equal(closed, afterLate);
            Assert.Equal(payDataBefore, payDataAfter);
        }
        finally
        {
            await Pay55CloseAsync(approver);
        }

        Assert.Equal("0", Pay55Count("\"Status\" in (0, 1)"));
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_55_vps_the_readiness_report_of_an_existing_run_lists_every_line_as_without_an_account()
    {
        var recorder = StartPay55("readiness",
            "Demo server, read-only: the bank readiness report of an existing run of the demo counts every line as without a pinned account and names each employee once; the run itself is as it was");
        var run = Sql("payroll_db",
            "select r.\"Id\" || '|' || (select count(*) from \"SalaryLines\" l where l.\"PayrollRunId\" = r.\"Id\") || '|' || " +
            "(select count(distinct l.\"EmployeeId\") from \"SalaryLines\" l where l.\"PayrollRunId\" = r.\"Id\") " +
            "from \"PayrollRuns\" r where exists (select 1 from \"SalaryLines\" l where l.\"PayrollRunId\" = r.\"Id\") order by r.\"PeriodStart\" desc, r.\"Id\" limit 1").SingleOrDefault()?.Split('|');
        AcceptanceGate.RequireData(recorder, run is { Length: 3 }, "لا يوجد تشغيل رواتب بأسطر على خادم العرض", "The demo has no payroll run with lines.");
        var payDataBefore = Pay55PayData();
        var password = DemoPassword();
        var (processor, _) = await SignInAsync(Pay55Processor, password, "/ui/payroll/");
        int status;
        string body;
        using (processor)
        using (var response = await processor.GetAsync($"https://{AppDomain}{Pay55Api}/runs/{run![0]}/bank-readiness"))
        {
            status = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync();
        }

        Assert.True(status == 200, $"The readiness report answered {status}.");
        using var json = JsonDocument.Parse(body);
        var report = json.RootElement;
        var lines = report.GetProperty("lines").GetInt32();
        var with = report.GetProperty("linesWithAccount").GetInt32();
        var without = report.GetProperty("linesWithoutAccount").GetInt32();
        var named = report.GetProperty("employeesWithoutAccount").EnumerateArray().Select(e => e.GetString()).ToList();
        var retired = report.GetProperty("employeesWithRetiredAccount").GetArrayLength();
        var payDataAfter = Pay55PayData();
        recorder.WriteScenarioDbChecks(new
        {
            run = run[0],
            linesInTheDatabase = run[1],
            employeesInTheDatabase = run[2],
            lines,
            linesWithAccount = with,
            linesWithoutAccount = without,
            employeesNamed = named.Count,
            employeesWithRetiredAccount = retired,
            payDataUnchanged = payDataBefore == payDataAfter
        });

        Assert.Equal(run[1], lines.ToString(CultureInfo.InvariantCulture));
        Assert.True(lines > 0);
        Assert.Equal(0, with);
        Assert.Equal(lines, without);
        Assert.Equal(run[2], named.Count.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(named.Count, named.Distinct().Count());
        Assert.Equal(0, retired);
        Assert.Equal(payDataBefore, payDataAfter);
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_55_vps_the_bank_account_screens_open_in_arabic_under_one_policy_with_the_number_masked()
    {
        var recorder = StartPay55("screens",
            "Demo server: with one test account waiting, the accounts' screens (the employee's accounts, the waiting list, the registration form, the run's page with its readiness card, the pay profiles) as e2e-payroll-processor and the verification page as e2e-payroll-approver, in a real Chromium: one CSP policy each, no securitypolicyviolation, Arabic and right-to-left, the employee and the registrar named by the registry's spans and no id printed, the number masked everywhere but on the approver's verification page, which is never stored; the account is rejected at the end");
        var employee = Pay55Employee(2);
        var run = Sql("payroll_db",
            "select r.\"Id\" from \"PayrollRuns\" r where exists (select 1 from \"SalaryLines\" l where l.\"PayrollRunId\" = r.\"Id\") order by r.\"PeriodStart\" desc, r.\"Id\" limit 1").SingleOrDefault();
        AcceptanceGate.RequireData(recorder, employee is not null && run is not null,
            "لا يوجد موظف بملف أجر فعّال أو لا يوجد تشغيل رواتب", "The demo has no employee with an active pay profile and no account, or no payroll run with lines.");
        var number = Pay55Iban(employee!, 4);
        var password = DemoPassword();
        using var processorApi = await Pay55ApiAsync(Pay55Processor, password);
        using var approverApi = await Pay55ApiAsync(Pay55Approver, password);
        var output = Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan55", "vps", "screens");
        var results = new List<Ux067PageResult>();
        var repeats = new List<string>();
        string accounts, waiting, verifyPage, verifyCache, runPage, registrar;
        try
        {
            var (registered, registeredError, registeredBody) = await Pay55PostAsync(processorApi, Pay55Accounts(employee!), Pay55Registration(number));
            var id = Pay55Id(registeredBody);
            Assert.True(registered == 201 && id.Length == 36, $"The registration answered {registered}: {registeredError}");
            registrar = Sql("payroll_db", $"select \"CreatedBy\" from \"EmployeeBankAccounts\" where \"Id\" = '{id}'").Single();

            Ux067KeyPage[] processorPages =
            [
                new("bank-accounts-of-employee", $"{Pay55Pages}?employeeId={employee}"),
                new("bank-accounts-waiting", $"{Pay55Pages}/Pending"),
                new("bank-account-form", $"{Pay55Pages}/Create?employeeId={employee}"),
                new("run-details-readiness", $"/ui/payroll/PayrollRuns/Details/{run}"),
                new("pay-profiles", "/ui/payroll/EmployeePayProfiles"),
            ];
            Ux067KeyPage[] approverPages =
            [
                new("bank-account-verification", $"{Pay55Pages}/Verify/{id}"),
                new("bank-accounts-waiting-approver", $"{Pay55Pages}/Pending"),
            ];
            (int Width, int Height)[] sizes = [(1366, 768), (390, 844)];

            // A page load, or the sign-in, fails when a network interface of the machine that runs the check changes under Chromium
            // (PLAN-54's note). So each session is opened once, each page is probed on its own, and a step the machine's network cut
            // is done again; what each page must show is unchanged.
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
                foreach (var (user, pages) in new[] { (Pay55Processor, processorPages), (Pay55Approver, approverPages) })
                {
                    var state = Path.Combine(Path.GetTempPath(), $"pay55-state-{Guid.NewGuid():N}.json");
                    states.Add(state);
                    await OnThisMachinesNetworkAsync($"sign-in {user}", async () =>
                    {
                        await Ux067PageProbe.SignInAndSaveStateAsync(browser, $"https://{AppDomain}", user, password, state);
                        return true;
                    });
                    // No screenshot of the verification page: it shows the whole number, and no image of it is kept anywhere.
                    var shots = Path.Combine(output, user == Pay55Approver ? "screenshots-approver" : "screenshots");
                    var (probed, again) = await Ux067PageProbe.ProbeEachAsync(browser, $"https://{AppDomain}", state, pages, sizes, shots, attemptsPerPage: 12);
                    results.AddRange(probed);
                    repeats.AddRange(again);
                }
            }
            finally
            {
                await browser.DisposeAsync();
                playwright.Dispose();
                foreach (var state in states.Where(File.Exists)) File.Delete(state);
                var approverShots = Path.Combine(output, "screenshots-approver");
                if (Directory.Exists(approverShots)) Directory.Delete(approverShots, recursive: true);
            }

            // What the server sends, read as text: Arabic, right-to-left, names by the registry's spans, the number masked.
            async Task<(string Html, string Cache)> PageAsync(HttpClient client, string path)
            {
                using var page = await client.GetAsync($"https://{AppDomain}{path}");
                Assert.True(page.IsSuccessStatusCode, $"GET {path} answered {(int)page.StatusCode}");
                return (WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync()), page.Headers.CacheControl?.ToString() ?? "");
            }

            (accounts, _) = await PageAsync(processorApi, $"{Pay55Pages}?employeeId={employee}");
            (waiting, _) = await PageAsync(processorApi, $"{Pay55Pages}/Pending");
            (runPage, _) = await PageAsync(processorApi, $"/ui/payroll/PayrollRuns/Details/{run}");
            (verifyPage, verifyCache) = await PageAsync(approverApi, $"{Pay55Pages}/Verify/{id}");
            using var processorVerify = await processorApi.GetAsync($"https://{AppDomain}{Pay55Pages}/Verify/{id}");
            var processorVerifyStatus = (int)processorVerify.StatusCode;
            var processorVerifyBody = await processorVerify.Content.ReadAsStringAsync();

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
                employee,
                account = id,
                masked = Pay55Masked(number),
                repeats,
                pages = results.Select(r => new { r.Name, r.Status, r.CspHeaders, r.CspPolicies, violations = r.Violations.Count, r.Passes }),
                rightToLeft = accounts.Contains("dir=\"rtl\"", StringComparison.Ordinal),
                arabic = accounts.Contains("lang=\"ar\"", StringComparison.Ordinal),
                employeeNamedByTheRegistry = accounts.Contains($"data-hr-employee-id=\"{employee}\"", StringComparison.OrdinalIgnoreCase),
                employeeIdPrinted = Visible(accounts).Contains(employee!, StringComparison.OrdinalIgnoreCase),
                registrarNamedByTheRegistry = accounts.Contains($"data-hr-user-id=\"{registrar}\"", StringComparison.OrdinalIgnoreCase),
                registrarIdPrinted = Visible(accounts).Contains(registrar, StringComparison.OrdinalIgnoreCase),
                processorPagesCarryTheWholeNumber = new[] { accounts, waiting, runPage }.Any(p => p.Contains(number, StringComparison.OrdinalIgnoreCase)),
                theApproverSeesTheWholeNumber = verifyPage.Contains(number, StringComparison.OrdinalIgnoreCase),
                verificationPageCache = verifyCache,
                verificationPageForTheProcessor = processorVerifyStatus,
                readinessCard = runPage.Contains("data-testid=\"run-bank-readiness\"", StringComparison.Ordinal)
            });

            Assert.Equal(processorPages.Length + approverPages.Length, results.Count);        // every page,
            Assert.All(results, r => Assert.Equal(sizes.Length, r.Screenshots.Count));       // opened at both sizes
            Assert.True(failures.Count == 0, "Screens failing the CSP check:\n" + string.Join("\n", failures));
            Assert.Contains("dir=\"rtl\"", accounts, StringComparison.Ordinal);
            Assert.Contains("lang=\"ar\"", accounts, StringComparison.Ordinal);
            Assert.Contains($"data-hr-employee-id=\"{employee}\"", accounts, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(employee!, Visible(accounts), StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"data-hr-user-id=\"{registrar}\"", accounts, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(registrar, Visible(accounts), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Pay55Masked(number), accounts, StringComparison.Ordinal);
            Assert.Contains(Pay55Masked(number), waiting, StringComparison.Ordinal);
            Assert.Contains("بانتظار التحقق", Visible(accounts), StringComparison.Ordinal);
            foreach (var page in new[] { accounts, waiting, runPage })
                Assert.DoesNotContain(number, page, StringComparison.OrdinalIgnoreCase);
            // The one place the number is whole: the approver's verification page, never stored; the processor does not reach it.
            Assert.Contains(number, verifyPage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no-store", verifyCache, StringComparison.OrdinalIgnoreCase);
            Assert.True(processorVerifyStatus is 403 or 404 || !processorVerifyBody.Contains(number, StringComparison.OrdinalIgnoreCase),
                $"The verification page answered {processorVerifyStatus} to the processor and carried the whole number.");
            Assert.Contains("data-testid=\"run-bank-readiness\"", runPage, StringComparison.Ordinal);
        }
        finally
        {
            await Pay55CloseAsync(approverApi);
        }

        Assert.Equal("0", Pay55Count("\"Status\" in (0, 1)"));
        recorder.WriteResult("Pass");
    }
}
