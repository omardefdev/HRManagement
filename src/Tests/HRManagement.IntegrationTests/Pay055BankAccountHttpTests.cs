using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payroll.Application.Features.BankAccounts;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.OrgScope;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-55 (§1, P-4, P-9): the employee's bank accounts over HTTP on Payroll.Web itself (PostgreSQL), with a principal restricted to a
/// branch (no persona holds payroll and a branch: the scope must hold anyway). In his unit a restricted user registers, lists and
/// verifies; another unit's employee and account are absent and answer 404; a central user sees every unit. Every write needs the
/// antiforgery token. The number is masked in every answer but the approver's verification page, and reaches neither audit.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay055BankAccountHttpTests(PostgreSqlFixture postgres, ITestOutputHelper output)
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Guid SomeoneElse = Guid.Parse("55000000-0000-4000-8000-0000000000c9");

    /// <summary>
    /// The host's users hold the processor's role (register); each verifies, rejects and retires with an approver's token of his own
    /// (PLAN-58, D-PAY-20: no token carries two conflicting payroll roles, so the same person acts with two tokens).
    /// </summary>
    private static readonly string CentralRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor}";

    private static readonly string ApproverRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}";

    private static readonly string RestrictedRoles = $"{HrManagementRoles.BranchManager},{CentralRoles}";

    private static readonly string RestrictedApproverRoles = $"{HrManagementRoles.BranchManager},{ApproverRoles}";

    /// <summary>A test IBAN whose check digits are computed: never a real account.</summary>
    private static string Iban(string bban) => "SY" + BankAccountNumberValidator.ComputeIbanCheckDigits("SY", bban) + bban;

    private static readonly string Number1 = Iban("000100000000000055HTTP01");
    private static readonly string Number2 = Iban("000100000000000055HTTP02");
    private static readonly string Number3 = Iban("000100000000000055HTTP03");

    /// <summary>Records the security audit events of the host (the JSONL sink of a service, replaced for the test).</summary>
    private sealed class RecordingAudit : ISecurityAuditSink
    {
        public ConcurrentQueue<SecurityAuditPayload> Events { get; } = new();

        public bool TryPublish(SecurityAuditPayload payload)
        {
            Events.Enqueue(payload);
            return true;
        }
    }

    /// <summary>Per branch: an employee with an active pay profile, and no account yet (the scope types the registry names are seeded).</summary>
    private static async Task<Dictionary<Type, Guid>> SeedAsync(PayrollDbContext db, Guid unit)
    {
        var employee = Guid.NewGuid();
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            Id = employee,
            TenantId = Tenant,
            EmployeeId = employee,
            BaseSalary = 4500m,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true,
            OrgUnitId = unit,
        });
        await db.SaveChangesAsync();
        return new Dictionary<Type, Guid> { [typeof(EmployeePayProfile)] = employee };
    }

    private Task<Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>> StartAsync(
        RecordingAudit? audit = null, IReadOnlyDictionary<string, string?>? settings = null) =>
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>.StartAsync(
            postgres, "PayrollDb", PayrollOrgScopeKeys.Registry, RestrictedRoles, CentralRoles, "/EmployeePayProfiles/Create", SeedAsync,
            testServices: audit is null
                ? null
                : services =>
                {
                    services.RemoveAll<ISecurityAuditSink>();
                    services.AddSingleton<ISecurityAuditSink>(audit);
                },
            settings: settings);

    private static async Task<HttpClient> WithApiTokenAsync(HttpClient client, string tokenPath = "/api/payroll/bank-accounts/antiforgery")
    {
        using var issued = await client.GetAsync(tokenPath);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        using var token = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add(
            token.RootElement.GetProperty("headerName").GetString()!, token.RootElement.GetProperty("requestToken").GetString());
        return client;
    }

    private static string Accounts(Guid employee) => $"/api/payroll/employees/{employee}/bank-accounts";

    private static object Registration(string number, string kind = "Iban", string? bankCode = null) =>
        new { accountKind = kind, accountNumber = number, bankCode, bankNameAr = "مصرف الاختبار", holderNameAr = "موظف الاختبار" };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<List<EmployeeBankAccount>> StoredAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, Guid employee) =>
        host.WithDbAsync(db => db.Set<EmployeeBankAccount>().AsNoTracking().Where(a => a.EmployeeId == employee).OrderBy(a => a.CreatedAt).ToListAsync());

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task One_person_registers_and_another_verifies_and_the_number_is_masked_in_every_answer_and_in_both_audits()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var audit = new RecordingAudit();
        await using var host = await StartAsync(audit);
        using var restricted = await WithApiTokenAsync(host.Restricted());
        using var restrictedApprover = await WithApiTokenAsync(host.RestrictedAs(RestrictedApproverRoles));
        using var central = await WithApiTokenAsync(host.Central());
        using var centralApprover = await WithApiTokenAsync(host.CentralAs(ApproverRoles));
        var (employee, _) = host.Records[typeof(EmployeePayProfile)];

        // The processor registers: 201, pending, and the answer carries no number.
        var typed = string.Join(' ', Number1.ToLowerInvariant().Chunk(4).Select(c => new string(c)));
        var created = await restricted.PostAsJsonAsync(Accounts(employee), Registration(typed));
        var createdText = await created.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.DoesNotContain(Number1, createdText, StringComparison.OrdinalIgnoreCase);
        var accountId = JsonDocument.Parse(createdText).RootElement.GetProperty("accountId").GetGuid();
        Assert.Equal("PendingVerification", JsonDocument.Parse(createdText).RootElement.GetProperty("status").GetString());
        var stored = Assert.Single(await StoredAsync(host, employee));
        Assert.Equal((Number1, BankAccountStatus.PendingVerification), (stored.AccountNumber, stored.Status));
        Assert.NotNull(stored.CreatedBy);

        // The list shows the number masked to its last four characters, to the registrar and to a central user alike.
        foreach (var client in new[] { restricted, central })
        {
            var list = await client.GetStringAsync(Accounts(employee));
            Assert.DoesNotContain(Number1, list, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(EmployeeBankAccount.Mask(Number1), list, StringComparison.Ordinal);
        }

        // The registrar verifying his own registration: 400, and the account still waits.
        var self = await restrictedApprover.PostAsJsonAsync($"{Accounts(employee)}/{accountId}/verify", new { verificationReference = "كتاب المصرف 118" });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal(BankAccountMessages.SelfVerification, (await JsonAsync(self)).GetProperty("error").GetString());
        // A verification without the bank's reference: 400.
        var noReference = await centralApprover.PostAsJsonAsync($"{Accounts(employee)}/{accountId}/verify", new { verificationReference = " " });
        Assert.Equal(HttpStatusCode.BadRequest, noReference.StatusCode);
        Assert.Equal(BankAccountStatus.PendingVerification, (await StoredAsync(host, employee)).Single().Status);

        // Another person verifies it against the bank's confirmation: in force.
        var verified = await centralApprover.PostAsJsonAsync($"{Accounts(employee)}/{accountId}/verify", new { verificationReference = "كتاب المصرف 118" });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal("Active", (await JsonAsync(verified)).GetProperty("status").GetString());
        stored = (await StoredAsync(host, employee)).Single();
        Assert.Equal((BankAccountStatus.Active, "كتاب المصرف 118"), (stored.Status, stored.VerificationReference));
        Assert.NotEqual(stored.CreatedBy, stored.VerifiedBy);

        // A replacement: the account in force stays in force until the replacement is verified, then it is retired.
        var replacement = await central.PostAsJsonAsync(Accounts(employee), Registration(Number2));
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        var replacementId = (await JsonAsync(replacement)).GetProperty("accountId").GetGuid();
        Assert.Equal(BankAccountStatus.Active, (await StoredAsync(host, employee)).Single(a => a.Id == accountId).Status);
        var second = await central.PostAsJsonAsync(Accounts(employee), Registration(Number3));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);                       // a second pending account
        var replaced = await restrictedApprover.PostAsJsonAsync($"{Accounts(employee)}/{replacementId}/verify", new { verificationReference = "كتاب المصرف 131" });
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(accountId, (await JsonAsync(replaced)).GetProperty("retiredAccountId").GetGuid());
        var both = await StoredAsync(host, employee);
        Assert.Equal(BankAccountStatus.Retired, both.Single(a => a.Id == accountId).Status);
        Assert.Equal(BankAccountStatus.Active, both.Single(a => a.Id == replacementId).Status);

        // Retiring needs a reason; then the employee has no account in force. A third registration is rejected with a reason.
        Assert.Equal(HttpStatusCode.BadRequest, (await centralApprover.PostAsJsonAsync($"{Accounts(employee)}/{replacementId}/retire", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await centralApprover.PostAsJsonAsync($"{Accounts(employee)}/{replacementId}/retire", new { reason = "أغلق الموظف حسابه" })).StatusCode);
        var third = await restricted.PostAsJsonAsync(Accounts(employee), Registration(Number3));
        var thirdId = (await JsonAsync(third)).GetProperty("accountId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await centralApprover.PostAsJsonAsync($"{Accounts(employee)}/{thirdId}/reject", new { reason = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await centralApprover.PostAsJsonAsync($"{Accounts(employee)}/{thirdId}/reject", new { reason = "الرقم لا يطابق كتاب المصرف" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await centralApprover.PostAsJsonAsync($"{Accounts(employee)}/{thirdId}/verify", new { verificationReference = "كتاب" })).StatusCode);
        Assert.DoesNotContain(await StoredAsync(host, employee), a => a.Status is BankAccountStatus.Active or BankAccountStatus.PendingVerification);

        // The entity audit log has every act of the three accounts, and never a number or a holder's name.
        var ids = (await StoredAsync(host, employee)).Select(a => a.Id).ToList();
        var logs = await host.WithDbAsync(db => db.Set<EntityAuditLog>().AsNoTracking()
            .Where(l => l.EntityType == nameof(EmployeeBankAccount) && ids.Contains(l.EntityId)).ToListAsync());
        Assert.True(logs.Count >= 7, $"{logs.Count} audit rows for 3 registrations and 4 decisions");
        Assert.All(logs, log =>
        {
            foreach (var number in new[] { Number1, Number2, Number3 })
                Assert.DoesNotContain(number, log.ChangedFields ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("موظف الاختبار", log.ChangedFields ?? string.Empty, StringComparison.Ordinal);
        });

        // The security audit has each act as a payroll override, by the account's id only.
        var events = audit.Events.Where(e => e.Resource?.StartsWith("BankAccount:", StringComparison.Ordinal) == true).ToList();
        Assert.All(events, e => Assert.Equal(SecurityAudit.EventTypes.PayrollOverride, e.EventType));
        Assert.Equal(
            new[] { "BankAccountRegister", "BankAccountVerify", "BankAccountRegister", "BankAccountVerify", "BankAccountRetire", "BankAccountRegister", "BankAccountReject" },
            events.Select(e => JsonDocument.Parse(e.DetailJson!).RootElement.GetProperty("action").GetString()));
        Assert.All(events, e =>
        {
            var text = JsonSerializer.Serialize(e);
            foreach (var number in new[] { Number1, Number2, Number3 })
            {
                Assert.DoesNotContain(number, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(number[^4..], e.Resource!, StringComparison.OrdinalIgnoreCase);
            }
        });
        output.WriteLine($"{logs.Count} entity audit rows and {events.Count} security audit events, none with a number.");
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_bad_number_is_400_with_an_arabic_message_and_no_row_and_a_write_without_the_antiforgery_token_is_refused()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        using var noToken = host.Central();
        using var noTokenApprover = host.CentralAs(ApproverRoles);
        using var central = await WithApiTokenAsync(host.Central());
        var (employee, _) = host.Records[typeof(EmployeePayProfile)];

        // No antiforgery token: every write of the API is refused before it runs, and nothing is written.
        Assert.Equal(HttpStatusCode.BadRequest, (await noToken.PostAsJsonAsync(Accounts(employee), Registration(Number1))).StatusCode);
        foreach (var act in new[] { "verify", "reject", "retire" })
            Assert.Equal(HttpStatusCode.BadRequest, (await noTokenApprover.PostAsJsonAsync($"{Accounts(employee)}/{Guid.NewGuid()}/{act}", new { reason = "سبب" })).StatusCode);
        Assert.Empty(await StoredAsync(host, employee));

        // A wrong check digit: 400, an Arabic message that does not repeat the number, and no row.
        var wrong = Number1[..3] + (Number1[3] == '9' ? '8' : (char)(Number1[3] + 1)) + Number1[4..];
        var refused = await central.PostAsJsonAsync(Accounts(employee), Registration(wrong));
        var refusedText = await refused.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(BankAccountMessages.IbanCheckDigits, JsonDocument.Parse(refusedText).RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain(wrong, refusedText, StringComparison.OrdinalIgnoreCase);

        // No bank is defined (as in production): a local number answers 400 in Arabic; an unknown kind too.
        var local = await central.PostAsJsonAsync(Accounts(employee), Registration("001234567890", "Local", "CBS"));
        Assert.Equal(HttpStatusCode.BadRequest, local.StatusCode);
        Assert.Equal(BankAccountMessages.UnknownBank, (await JsonAsync(local)).GetProperty("error").GetString());
        var unknownKind = await central.PostAsJsonAsync(Accounts(employee), Registration(Number1, "Wire"));
        Assert.Equal(HttpStatusCode.BadRequest, unknownKind.StatusCode);
        Assert.Equal(BankAccountMessages.KindUnknown, (await JsonAsync(unknownKind)).GetProperty("error").GetString());
        Assert.Empty(await StoredAsync(host, employee));

        // An employee Payroll does not know: 404.
        Assert.Equal(HttpStatusCode.NotFound, (await central.GetAsync(Accounts(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await central.PostAsJsonAsync(Accounts(Guid.NewGuid()), Registration(Number1))).StatusCode);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Another_units_employee_and_account_are_absent_and_404_for_a_restricted_user_and_a_central_user_sees_every_unit()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        using var restricted = await WithApiTokenAsync(host.Restricted());
        using var restrictedApprover = await WithApiTokenAsync(host.RestrictedAs(RestrictedApproverRoles));
        using var central = await WithApiTokenAsync(host.Central());
        var (own, foreign) = host.Records[typeof(EmployeePayProfile)];

        // A central user registers an account for each unit's employee.
        var ownAccount = (await JsonAsync(await central.PostAsJsonAsync(Accounts(own), Registration(Number1)))).GetProperty("accountId").GetGuid();
        var foreignAccount = (await JsonAsync(await central.PostAsJsonAsync(Accounts(foreign), Registration(Number2)))).GetProperty("accountId").GetGuid();

        // In his unit: allowed. Outside it: the employee does not exist for him, by the API and on every page.
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync(Accounts(own))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await restricted.GetAsync(Accounts(foreign))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await restricted.PostAsJsonAsync(Accounts(foreign), Registration(Number3))).StatusCode);
        foreach (var (act, body) in new (string, object)[]
                 {
                     ("verify", new { verificationReference = "كتاب المصرف" }), ("reject", new { reason = "سبب" }), ("retire", new { reason = "سبب" }),
                 })
        {
            // By the foreign employee's route, and by his own employee's route with the foreign account's id.
            Assert.Equal(HttpStatusCode.NotFound, (await restrictedApprover.PostAsJsonAsync($"{Accounts(foreign)}/{foreignAccount}/{act}", body)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await restrictedApprover.PostAsJsonAsync($"{Accounts(own)}/{foreignAccount}/{act}", body)).StatusCode);
        }

        var untouched = (await StoredAsync(host, foreign)).Single();
        Assert.Equal((BankAccountStatus.PendingVerification, foreignAccount), (untouched.Status, untouched.Id));

        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync($"/EmployeeBankAccounts?employeeId={own}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await restricted.GetAsync($"/EmployeeBankAccounts?employeeId={foreign}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await restricted.GetAsync($"/EmployeeBankAccounts/Create?employeeId={foreign}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await restrictedApprover.GetAsync($"/EmployeeBankAccounts/Verify/{foreignAccount}")).StatusCode);

        // The list of accounts that wait: his unit's only; a central user sees both units.
        var pending = await restricted.GetStringAsync("/EmployeeBankAccounts/Pending");
        Assert.Contains(own.ToString("D"), pending, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(foreign.ToString("D"), pending, StringComparison.OrdinalIgnoreCase);
        var all = await central.GetStringAsync("/EmployeeBankAccounts/Pending");
        Assert.Contains(own.ToString("D"), all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(foreign.ToString("D"), all, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await central.GetAsync(Accounts(foreign))).StatusCode);

        // The pages mask the number; only the approver's verification page shows it whole, and it is never stored by a browser.
        foreach (var page in new[] { pending, all, await restricted.GetStringAsync($"/EmployeeBankAccounts?employeeId={own}") })
        {
            Assert.DoesNotContain(Number1, page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Number2, page, StringComparison.OrdinalIgnoreCase);
        }

        using var verification = await restrictedApprover.GetAsync($"/EmployeeBankAccounts/Verify/{ownAccount}");
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        Assert.Contains(Number1, await verification.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(verification.Headers.CacheControl?.NoStore, "The verification page must not be stored");

        // A user with the module role alone opens the lists, and neither the registration form nor the verification page.
        using var reader = host.CentralWith(HrManagementRoles.Payroll, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/EmployeeBankAccounts?employeeId={own}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync($"/EmployeeBankAccounts/Verify/{ownAccount}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync($"/EmployeeBankAccounts/Create?employeeId={own}")).StatusCode);
    }

    // --- P-9: the plan's «the branch manager sees it in the pay profile list» (AC-PAY-55b), with a restricted principal ---

    private static async Task InHostAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, Func<IServiceProvider, Task> work)
    {
        await using var scope = host.Host.Services.CreateAsyncScope();
        TenantIdFlow.Set(Tenant);
        try
        {
            await work(scope.ServiceProvider);
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private static Task<EmployeePayProfile> ActiveProfileAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, Guid employee) =>
        host.WithDbAsync(db => db.Set<EmployeePayProfile>().AsNoTracking().SingleAsync(p => p.EmployeeId == employee && p.IsActive));

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task After_a_salary_change_and_after_a_contract_activation_the_new_version_keeps_the_unit_and_the_regime_and_the_branch_still_lists_the_employee()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        using var restricted = await WithApiTokenAsync(host.Restricted());
        using var central = await WithApiTokenAsync(host.Central());
        using var centralApprover = await WithApiTokenAsync(host.CentralAs(ApproverRoles));
        var (own, foreign) = host.Records[typeof(EmployeePayProfile)];
        // The seed's profiles carry a unit; give both a regime too, so the copy is proved on a value.
        await host.WithDbAsync(db => db.Set<EmployeePayProfile>().ExecuteUpdateAsync(s => s.SetProperty(p => p.RegimeCode, "law50")));

        async Task AssertListedInHisBranchOnlyAsync(string step)
        {
            var list = await restricted.GetStringAsync("/EmployeePayProfiles");
            Assert.True(list.Contains(own.ToString("D"), StringComparison.OrdinalIgnoreCase), $"{step}: the branch's list lost its employee");
            Assert.False(list.Contains(foreign.ToString("D"), StringComparison.OrdinalIgnoreCase), $"{step}: the branch's list shows another unit's employee");
            Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync($"/EmployeePayProfiles/History?employeeId={own}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await restricted.GetAsync($"/EmployeePayProfiles/History?employeeId={foreign}")).StatusCode);
            var everyone = await central.GetStringAsync("/EmployeePayProfiles");
            Assert.Contains(own.ToString("D"), everyone, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(foreign.ToString("D"), everyone, StringComparison.OrdinalIgnoreCase);
        }

        await AssertListedInHisBranchOnlyAsync("before");

        // 1. A salary change by PLAN-53's transaction: the restricted user requests it for the first day of the current month
        //    (Damascus), a central user approves it, and it is applied at once.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Damascus"));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var submitted = await restricted.PostAsJsonAsync("/api/payroll/pay-profile-changes", new
        {
            employeeId = own,
            proposedBaseSalary = 5100m,
            effectiveFrom = monthStart.ToString("yyyy-MM-dd"),
            reason = "ترفيع استثنائي",
            legalReference = "القرار 112 لعام 2026",
        });
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var requestId = (await JsonAsync(submitted)).GetProperty("requestId").GetGuid();
        var approved = await centralApprover.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{requestId}/approve", new { note = "موافق" });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("Applied", (await JsonAsync(approved)).GetProperty("state").GetString());

        var afterChange = await ActiveProfileAsync(host, own);
        Assert.Equal((5100m, (Guid?)host.OwnUnit, "law50"), (afterChange.BaseSalary, afterChange.OrgUnitId, afterChange.RegimeCode));
        Assert.NotEqual(own, afterChange.Id);                                    // a new version (the seed's profile id is the employee id)
        await AssertListedInHisBranchOnlyAsync("after the salary change");

        // 2. A contract activation, as its consumer handles it: the version it makes keeps the unit and the regime too.
        var contract = Guid.NewGuid();
        var start = monthStart.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await InHostAsync(host, services => services.GetRequiredService<MediatR.IMediator>().Send(
            new Payroll.Application.Features.InboundEvents.HandleContractActivatedCommand(contract, own, "PERM", start, 5300m, null)));
        var afterActivation = await ActiveProfileAsync(host, own);
        Assert.Equal((5300m, (Guid?)host.OwnUnit, "law50", start), (afterActivation.BaseSalary, afterActivation.OrgUnitId, afterActivation.RegimeCode, afterActivation.EffectiveFrom));
        Assert.Equal(start.AddDays(-1), (await host.WithDbAsync(db => db.Set<EmployeePayProfile>().AsNoTracking().SingleAsync(p => p.Id == afterChange.Id))).EffectiveTo);
        await AssertListedInHisBranchOnlyAsync("after the contract activation");

        // The activation is an applied request of his unit: it shows in his branch's list of requests, and its event is in the outbox.
        var requests = await restricted.GetStringAsync($"/api/payroll/pay-profile-changes?employeeId={own}");
        Assert.Contains(contract.ToString("D"), requests, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ContractActivated", requests, StringComparison.Ordinal);

        // The same for another unit's employee: his version keeps his unit, and the restricted user still does not see him.
        await InHostAsync(host, services => services.GetRequiredService<MediatR.IMediator>().Send(
            new Payroll.Application.Features.InboundEvents.HandleContractActivatedCommand(Guid.NewGuid(), foreign, "PERM", start, 5300m, null)));
        Assert.Equal((Guid?)host.ForeignUnit, (await ActiveProfileAsync(host, foreign)).OrgUnitId);
        await AssertListedInHisBranchOnlyAsync("after another unit's activation");
    }

    // --- P-8: the repair of old pay profiles ---

    private const string Repair = "/api/payroll/admin/backfill-profile-orgunit";

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_repair_is_an_administrators_central_act_with_a_dry_run_first_and_brings_the_employee_back_into_his_branchs_list()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var (own, _) = host.Records[typeof(EmployeePayProfile)];
        // As a build before PLAN-55 left it: the active version (made by a contract activation) has no unit and no regime, and the
        // employee's earlier profile carries both.
        var activeId = await host.WithDbAsync(async db =>
        {
            var active = await db.Set<EmployeePayProfile>().SingleAsync(p => p.EmployeeId == own);
            active.OrgUnitId = null;
            active.RegimeCode = null;
            db.Add(new EmployeePayProfile
            {
                TenantId = Tenant,
                EmployeeId = own,
                IsActive = false,
                BaseSalary = 4000m,
                OrgUnitId = host.OwnUnit,
                RegimeCode = "law50",
                EffectiveFrom = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EffectiveTo = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
            return active.Id;
        });
        using var restricted = host.Restricted();
        Assert.DoesNotContain(own.ToString("D"), await restricted.GetStringAsync("/EmployeePayProfiles"), StringComparison.OrdinalIgnoreCase);

        // Not an administrator (a processor and approver): neither the token nor the repair.
        using var payroll = await WithApiTokenAsync(host.Central());
        Assert.Equal(HttpStatusCode.Forbidden, (await payroll.GetAsync("/api/payroll/admin/antiforgery")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await payroll.PostAsync($"{Repair}?dryRun=false", null)).StatusCode);
        // The module role alone: the same.
        using var reader = host.CentralWith(HrManagementRoles.Payroll, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/payroll/admin/antiforgery")).StatusCode);
        // An administrator without the antiforgery token: refused before it runs.
        using var noToken = host.CentralWith(HrManagementRoles.Admin, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await noToken.PostAsync($"{Repair}?dryRun=false", null)).StatusCode);

        async Task<(Guid? Unit, string? Regime)> StoredAsync()
        {
            var profile = await host.WithDbAsync(db => db.Set<EmployeePayProfile>().AsNoTracking().SingleAsync(p => p.Id == activeId));
            return (profile.OrgUnitId, profile.RegimeCode);
        }

        Assert.Equal(((Guid?)null, (string?)null), await StoredAsync());

        // The dry run is the default: it lists the profile and changes nothing.
        using var admin = await WithApiTokenAsync(host.CentralWith(HrManagementRoles.Admin, Guid.NewGuid()), "/api/payroll/admin/antiforgery");
        var dry = await JsonAsync(await admin.PostAsync(Repair, null));
        Assert.True(dry.GetProperty("dryRun").GetBoolean());
        Assert.Equal((1, 0), (dry.GetProperty("found").GetInt32(), dry.GetProperty("changed").GetInt32()));
        var listed = dry.GetProperty("items")[0];
        Assert.Equal((activeId, own, host.OwnUnit, "law50"),
            (listed.GetProperty("profileId").GetGuid(), listed.GetProperty("employeeId").GetGuid(), listed.GetProperty("orgUnitId").GetGuid(), listed.GetProperty("regimeCode").GetString()));
        Assert.Equal(((Guid?)null, (string?)null), await StoredAsync());

        // The real run changes exactly that, and the branch lists its employee again.
        var real = await JsonAsync(await admin.PostAsync($"{Repair}?dryRun=false", null));
        Assert.False(real.GetProperty("dryRun").GetBoolean());
        Assert.Equal((1, 1), (real.GetProperty("found").GetInt32(), real.GetProperty("changed").GetInt32()));
        Assert.Equal(((Guid?)host.OwnUnit, "law50"), await StoredAsync());
        Assert.Contains(own.ToString("D"), await restricted.GetStringAsync("/EmployeePayProfiles"), StringComparison.OrdinalIgnoreCase);
        var audits = await host.WithDbAsync(db => db.Set<EntityAuditLog>().AsNoTracking()
            .Where(a => a.Action == Payroll.Application.Features.PayProfileRepair.BackfillProfileOrgUnitHandler.AuditAction).ToListAsync());
        Assert.Equal(real.GetProperty("auditEntryId").GetGuid(), Assert.Single(audits).Id);

        // A second run finds nothing and writes no second audit entry.
        var again = await JsonAsync(await admin.PostAsync($"{Repair}?dryRun=false", null));
        Assert.Equal((0, 0), (again.GetProperty("found").GetInt32(), again.GetProperty("changed").GetInt32()));
        Assert.Single(await host.WithDbAsync(db => db.Set<EntityAuditLog>().AsNoTracking()
            .Where(a => a.Action == Payroll.Application.Features.PayProfileRepair.BackfillProfileOrgUnitHandler.AuditAction).ToListAsync()));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_repair_is_a_central_act_whoever_the_settings_give_its_policy_to()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        // An administrator is never restricted to a branch, so the stance is shown with the policy's roles read from configuration:
        // an agency that gives Payroll.Repair to its payroll approvers. A restricted approver passes the policy and is still refused.
        await using var host = await StartAsync(settings: new Dictionary<string, string?>
        {
            ["Authorization:Policies:Payroll.Repair:Roles:0"] = HrManagementRoles.PayrollApprover,
        });
        using var restricted = host.RestrictedAs(RestrictedApproverRoles);
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.GetAsync("/api/payroll/admin/antiforgery")).StatusCode);
        Assert.False((await restricted.PostAsync($"{Repair}?dryRun=false", null)).IsSuccessStatusCode);

        // A central approver passes both: the roles of the policy are the configured ones (and an administrator is no longer one of them).
        using var central = await WithApiTokenAsync(host.CentralAs(ApproverRoles), "/api/payroll/admin/antiforgery");
        var dry = await central.PostAsync(Repair, null);
        Assert.Equal(HttpStatusCode.OK, dry.StatusCode);
        Assert.True((await JsonAsync(dry)).GetProperty("dryRun").GetBoolean());
        using var admin = host.CentralWith(HrManagementRoles.Admin, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/payroll/admin/antiforgery")).StatusCode);
    }
}
