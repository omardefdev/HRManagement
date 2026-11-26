using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.ReferenceChanges;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.OrgScope;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-53 (§2 rule 3, P-9; the IAM study's note 3): the branch scope of the salary change requests and of the pay profiles, over HTTP
/// on Payroll.Web itself (PostgreSQL). First the test PLAN-52 generates from the registry: on every action of the scoped controllers a
/// foreign request or employee is 404, nothing changes, and no page shows one. Then the plan's own walk: a restricted payroll user
/// submits, lists, opens and decides in his unit; another unit's requests are absent and answer 404; a central user sees every unit.
/// </summary>
[Collection("PostgreSql")]
public sealed class Iam053PayrollScopeHttpTests(PostgreSqlFixture postgres, ITestOutputHelper output)
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Guid SomeoneElse = Guid.Parse("53000000-0000-4000-8000-0000000000c9");

    /// <summary>
    /// The host's users hold the processor's role (submit, cancel); each decides with an approver's token of his own (PLAN-58, D-PAY-20: no
    /// token carries two conflicting payroll roles, so the same person acts with two tokens).
    /// </summary>
    private static readonly string CentralRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor}";

    private static readonly string ApproverRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}";

    /// <summary>A payroll processor restricted to a branch: no such persona exists yet (wave 2), the scope must hold anyway.</summary>
    private static readonly string RestrictedRoles = $"{HrManagementRoles.BranchManager},{CentralRoles}";

    /// <summary>The same restricted person with an approver's token.</summary>
    private static readonly string RestrictedApproverRoles = $"{HrManagementRoles.BranchManager},{ApproverRoles}";

    /// <summary>Per branch: an employee with an active profile (his profile's id is his employee id, so the generated test finds the row)
    /// and a pending request another user made for the first day of next month.</summary>
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
        var request = new PayProfileChangeRequest
        {
            TenantId = Tenant,
            EmployeeId = employee,
            OrgUnitId = unit,
            ChangeKind = PayProfileChangeKind.BaseSalary,
            Source = PayProfileChangeSource.Manual,
            // As the submit command writes it: the request names the profile version it was made against (the applier checks it).
            CurrentProfileId = employee,
            CurrentBaseSalary = 4500m,
            ProposedBaseSalary = 5000m,
            EffectiveFrom = NextMonth(),
            Reason = "ترفيع",
            LegalReference = "القرار 5 لعام 2026",
            State = PayProfileChangeState.PendingApproval,
            RequestedBy = SomeoneElse,
            RequestedAt = DateTime.UtcNow,
        };
        db.PayProfileChangeRequests.Add(request);
        // PLAN-55: the employee's bank account is scoped by its employee too (a pending one, registered by another user).
        var account = new EmployeeBankAccount
        {
            TenantId = Tenant,
            EmployeeId = employee,
            AccountKind = BankAccountKind.Iban,
            AccountNumber = "SY" + Payroll.Domain.Services.BankAccountNumberValidator.ComputeIbanCheckDigits("SY", employee.ToString("N")[..20].ToUpperInvariant())
                            + employee.ToString("N")[..20].ToUpperInvariant(),
            BankNameAr = "مصرف الاختبار",
            Status = BankAccountStatus.PendingVerification,
            CreatedBy = SomeoneElse,
        };
        db.Add(account);
        await db.SaveChangesAsync();
        return new Dictionary<Type, Guid>
        {
            [typeof(EmployeePayProfile)] = employee,
            [typeof(PayProfileChangeRequest)] = request.Id,
            [typeof(EmployeeBankAccount)] = account.Id,
        };
    }

    /// <summary>The first day of the month after next (in UTC): a manual request for it is always accepted and, approved, always scheduled.</summary>
    private static DateOnly NextMonth()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new DateOnly(today.Year, today.Month, 1).AddMonths(2);
    }

    private Task<Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>> StartAsync() =>
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>.StartAsync(
            postgres, "PayrollDb", PayrollOrgScopeKeys.Registry, RestrictedRoles, CentralRoles, "/EmployeePayProfiles/Create", SeedAsync);

    private static async Task<Guid> AddEmployeeAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, Guid? unit)
    {
        var employee = Guid.NewGuid();
        await host.WithDbAsync(async db =>
        {
            db.Set<EmployeePayProfile>().Add(new EmployeePayProfile
            {
                TenantId = Tenant,
                EmployeeId = employee,
                BaseSalary = 4500m,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true,
                OrgUnitId = unit,
            });
            return await db.SaveChangesAsync();
        });
        return employee;
    }

    /// <summary>
    /// The client with the antiforgery token of the salary change API on every request (the client keeps the cookie). The API's
    /// writes validate it: the API is reached with a gateway session as well as with a bearer token.
    /// </summary>
    private static async Task<HttpClient> WithApiTokenAsync(HttpClient client)
    {
        using var issued = await client.GetAsync("/api/payroll/pay-profile-changes/antiforgery");
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        using var token = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add(
            token.RootElement.GetProperty("headerName").GetString()!, token.RootElement.GetProperty("requestToken").GetString());
        return client;
    }

    private static Task<PayProfileChangeState> StateAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, Guid request) =>
        host.WithDbAsync(db => db.Set<PayProfileChangeRequest>().AsNoTracking().Where(r => r.Id == request).Select(r => r.State).SingleAsync());

    private static Task<int> RequestCountAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host) =>
        host.WithDbAsync(db => db.Set<PayProfileChangeRequest>().CountAsync());

    private static object Body(Guid employee, decimal salary = 5100m) => new
    {
        employeeId = employee,
        proposedBaseSalary = salary,
        effectiveFrom = NextMonth().ToString("yyyy-MM-dd"),
        reason = "ترفيع استثنائي",
        legalReference = "القرار 112 لعام 2026",
    };

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Payroll_a_foreign_request_or_employee_is_404_on_every_scoped_action_and_no_page_shows_one()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();

        // PLAN-58: once with the processor's token and once with the approver's, so that every act is reached by the role it asks for.
        var asProcessor = await Iam052ScopeHttp.RunAsync(host);
        // The approver opens no request form; his token is read from his own unit's request, whose page carries the decision's forms.
        var asApprover = await Iam052ScopeHttp.RunAsync(host.WithRoles(
            RestrictedApproverRoles, ApproverRoles, $"/PayProfileChanges/Details/{host.Records[typeof(PayProfileChangeRequest)].Own}"));
        // A controller one role cannot reach is exercised by the other: «no action» counts only when neither pass reached it.
        const string NoAction = "no action could be exercised (a seed or a route the generator lacks)";
        var outcome = new Iam052ScopeHttp.Outcome(
            [.. asProcessor.Offenders.Concat(asApprover.Offenders).Where(o => !o.EndsWith(NoAction, StringComparison.Ordinal))],
            [.. asProcessor.Exercised, .. asApprover.Exercised],
            [.. asProcessor.Skipped, .. asApprover.Skipped]);
        foreach (var controller in asProcessor.Offenders.Concat(asApprover.Offenders)
                     .Where(o => o.EndsWith(NoAction, StringComparison.Ordinal))
                     .Select(o => o[..o.IndexOf(':', StringComparison.Ordinal)])
                     .Distinct())
        {
            Assert.True(outcome.Exercised.Any(line => line.StartsWith(controller + ".", StringComparison.Ordinal)), $"{controller}: no action exercised by either role");
        }

        foreach (var line in outcome.Exercised)
            output.WriteLine("ok   " + line);
        foreach (var line in outcome.Skipped)
            output.WriteLine("skip " + line);
        var unseeded = host.Registry.Keys.Keys.Where(t => !host.Records.ContainsKey(t)).Select(t => t.Name).ToList();
        Assert.True(unseeded.Count == 0, "No seed for: " + string.Join(", ", unseeded));
        Assert.True(outcome.Offenders.Count == 0, string.Join("\n", outcome.Offenders));
        // The scoped controllers were all exercised: the two request controllers, the pay profiles, and PLAN-55's two bank account ones.
        foreach (var controller in new[]
                 {
                     "PayProfileChangesController", "PayProfileChangesApiController", "EmployeePayProfilesController",
                     "EmployeeBankAccountsController", "EmployeeBankAccountsApiController",
                 })
            Assert.Contains(outcome.Exercised, line => line.StartsWith(controller + ".", StringComparison.Ordinal));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_restricted_payroll_user_lists_opens_and_decides_his_units_requests_and_another_units_are_absent()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        using var restricted = await WithApiTokenAsync(host.Restricted());
        using var central = await WithApiTokenAsync(host.Central());
        using var restrictedApprover = await WithApiTokenAsync(host.RestrictedAs(RestrictedApproverRoles));
        using var centralApprover = await WithApiTokenAsync(host.CentralAs(ApproverRoles));
        var (ownRequest, foreignRequest) = host.Records[typeof(PayProfileChangeRequest)];
        var (ownEmployee, foreignEmployee) = host.Records[typeof(EmployeePayProfile)];

        // The list: his unit's request only, over the API and on the page.
        var list = await restricted.GetStringAsync("/api/payroll/pay-profile-changes");
        Assert.Contains(ownRequest.ToString("D"), list, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(foreignRequest.ToString("D"), list, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(foreignEmployee.ToString("D"), list, StringComparison.OrdinalIgnoreCase);
        var page = await restricted.GetStringAsync("/PayProfileChanges");
        Assert.Contains(ownEmployee.ToString("D"), page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(foreignEmployee.ToString("D"), page, StringComparison.OrdinalIgnoreCase);

        // A central user with the same payroll roles sees both units.
        var all = await central.GetStringAsync("/api/payroll/pay-profile-changes");
        Assert.Contains(ownRequest.ToString("D"), all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(foreignRequest.ToString("D"), all, StringComparison.OrdinalIgnoreCase);

        // Details: his own opens; the other unit's answers 404, over the API and on the page.
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync($"/api/payroll/pay-profile-changes/{ownRequest}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync($"/PayProfileChanges/Details/{ownRequest}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await restricted.GetAsync($"/api/payroll/pay-profile-changes/{foreignRequest}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await restricted.GetAsync($"/PayProfileChanges/Details/{foreignRequest}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await central.GetAsync($"/api/payroll/pay-profile-changes/{foreignRequest}")).StatusCode);

        // Deciding: the other unit's request is 404 for every action and stays pending; his own is approved (scheduled: next month).
        foreach (var (by, action) in new[] { (restrictedApprover, "approve"), (restrictedApprover, "reject"), (restricted, "cancel") })
        {
            var refused = await by.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{foreignRequest}/{action}", new { note = "ملاحظة", reason = "سبب" });
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        Assert.Equal(PayProfileChangeState.PendingApproval, await StateAsync(host, foreignRequest));
        var approved = await restrictedApprover.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{ownRequest}/approve", new { note = "موافق" });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("Scheduled", JsonDocument.Parse(await approved.Content.ReadAsStringAsync()).RootElement.GetProperty("state").GetString());
        Assert.Equal(PayProfileChangeState.Scheduled, await StateAsync(host, ownRequest));

        // The pay profiles: his unit's list, and the other unit's employee is 404 by id.
        var profiles = await restricted.GetStringAsync("/EmployeePayProfiles");
        Assert.Contains(ownEmployee.ToString("D"), profiles, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(foreignEmployee.ToString("D"), profiles, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync($"/EmployeePayProfiles/History?employeeId={ownEmployee}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await restricted.GetAsync($"/EmployeePayProfiles/History?employeeId={foreignEmployee}")).StatusCode);

        // The dashboard counts his branch only: one request left pending in the agency (the other unit's), none in his.
        Assert.Equal(0, PendingTile(await restricted.GetStringAsync("/Dashboard")));
        Assert.Equal(1, PendingTile(await central.GetStringAsync("/Dashboard")));

        // Applying every due request of the agency is a central user's action.
        Assert.Equal(HttpStatusCode.Forbidden, (await restrictedApprover.PostAsync("/api/payroll/pay-profile-changes/activate-due", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await centralApprover.PostAsync("/api/payroll/pay-profile-changes/activate-due", null)).StatusCode);
    }

    private static int PendingTile(string html)
    {
        var match = Regex.Match(html, @"<h3>(\d+)</h3>\s*<p>طلبات تغيير راتب بانتظار الاعتماد</p>");
        Assert.True(match.Success, "The dashboard has no pending salary changes tile");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_restricted_payroll_user_submits_for_his_units_employee_only_through_the_api_the_alias_and_the_form()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        using var restricted = await WithApiTokenAsync(host.Restricted());
        using var central = await WithApiTokenAsync(host.Central());
        using var restrictedApprover = await WithApiTokenAsync(host.RestrictedAs(RestrictedApproverRoles));
        using var centralApprover = await WithApiTokenAsync(host.CentralAs(ApproverRoles));
        var mine = await AddEmployeeAsync(host, host.OwnUnit);
        var alsoMine = await AddEmployeeAsync(host, host.OwnUnit);
        var theirs = await AddEmployeeAsync(host, host.ForeignUnit);
        var centralEmployee = await AddEmployeeAsync(host, unit: null);
        var before = await RequestCountAsync(host);

        // Another unit's employee, an employee with no unit (central) and an unknown employee: 404, and no row.
        foreach (var employee in new[] { theirs, centralEmployee, Guid.NewGuid() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await restricted.PostAsJsonAsync("/api/payroll/pay-profile-changes", Body(employee))).StatusCode);
            var alias = await restricted.PutAsJsonAsync($"/api/payroll/employees/{employee}/pay-profile",
                new { baseSalary = 5100m, effectiveFrom = NextMonth().ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), changeReason = "ترفيع", legalReference = "القرار 1" });
            Assert.Equal(HttpStatusCode.NotFound, alias.StatusCode);
        }

        Assert.Equal(before, await RequestCountAsync(host));

        // His unit's employee: the API creates a pending request that copies the unit.
        var created = await restricted.PostAsJsonAsync("/api/payroll/pay-profile-changes", Body(mine));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("PendingApproval", createdBody.GetProperty("state").GetString());
        var createdId = createdBody.GetProperty("requestId").GetGuid();
        var stored = await host.WithDbAsync(db => db.Set<PayProfileChangeRequest>().AsNoTracking().SingleAsync(r => r.Id == createdId));
        Assert.Equal((host.OwnUnit, PayProfileChangeSource.Api, 4500m, 5100m), (stored.OrgUnitId!.Value, stored.Source, stored.CurrentBaseSalary!.Value, stored.ProposedBaseSalary!.Value));

        // The PUT alias answers 202 with the request, and writes no pay profile.
        var accepted = await restricted.PutAsJsonAsync($"/api/payroll/employees/{alsoMine}/pay-profile",
            new { baseSalary = 5200m, effectiveFrom = NextMonth().ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), changeReason = "ترفيع", legalReference = "القرار 2" });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal("PendingApproval", JsonDocument.Parse(await accepted.Content.ReadAsStringAsync()).RootElement.GetProperty("state").GetString());
        var profile = await host.WithDbAsync(db => db.Set<EmployeePayProfile>().AsNoTracking().SingleAsync(p => p.EmployeeId == alsoMine));
        Assert.Equal((4500m, true), (profile.BaseSalary, profile.IsActive));

        // The same answers as everywhere: a second open request is 409, a broken rule 400, his own request 403.
        Assert.Equal(HttpStatusCode.Conflict, (await restricted.PostAsJsonAsync("/api/payroll/pay-profile-changes", Body(mine, 5300m))).StatusCode);
        var midMonth = await central.PostAsJsonAsync("/api/payroll/pay-profile-changes", new
        {
            employeeId = theirs,
            proposedBaseSalary = 5100m,
            effectiveFrom = NextMonth().AddDays(9).ToString("yyyy-MM-dd"),
            reason = "ترفيع",
            legalReference = "القرار 3",
        });
        Assert.Equal(HttpStatusCode.BadRequest, midMonth.StatusCode);
        Assert.Matches(@"\p{IsArabic}", JsonDocument.Parse(await midMonth.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString()!);
        var own = await restrictedApprover.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{createdId}/approve", new { note = "" });
        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.Equal("لا يجوز اعتماد الطلب من طالبه", JsonDocument.Parse(await own.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
        Assert.Equal(PayProfileChangeState.PendingApproval, await StateAsync(host, createdId));

        // The central user, another person, approves it; then the requester cancels his scheduled request.
        Assert.Equal(HttpStatusCode.OK, (await centralApprover.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{createdId}/approve", new { note = "موافق" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await restricted.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{createdId}/cancel", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await restricted.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{createdId}/cancel", new { reason = "أُلغي القرار" })).StatusCode);
        Assert.Equal(PayProfileChangeState.Cancelled, await StateAsync(host, createdId));
    }

    /// <summary>The number a grade's page prints after a label (the review's finding: the counts were the agency's for everyone).</summary>
    private static int CountAfter(string html, string label)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, System.Text.RegularExpressions.Regex.Escape(label) + @"</dt>\s*<dd[^>]*>\s*(\d+)");
        Assert.True(match.Success, $"«{label}» is not on the page.");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_pay_profiles_counted_on_a_grades_page_and_on_its_change_request_are_the_callers_branchs()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        using var restricted = host.Restricted();
        using var central = host.Central();

        // A grade of the agency with bounds 4000..6000; its own unit has one profile inside and one outside, the other unit two
        // inside and two outside, and one central profile (no unit) outside.
        var grade = Guid.NewGuid();
        var request = Guid.NewGuid();
        await host.WithDbAsync(async db =>
        {
            var scale = new SalaryScale { TenantId = Tenant, Code = "S-53", NameAr = "سلم", EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            db.Add(scale);
            db.Add(new PayGrade { Id = grade, TenantId = Tenant, SalaryScale = scale, GradeCode = "G53", GradeNameAr = "درجة", BaseSalary = 4500m, MinSalary = 4000m, MaxSalary = 6000m });
            void Profile(Guid? unit, decimal salary) => db.Add(new EmployeePayProfile
            {
                TenantId = Tenant,
                EmployeeId = Guid.NewGuid(),
                PayGradeId = grade,
                BaseSalary = salary,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true,
                OrgUnitId = unit,
            });
            Profile(host.OwnUnit, 4500m);
            Profile(host.OwnUnit, 7000m);
            Profile(host.ForeignUnit, 4500m);
            Profile(host.ForeignUnit, 5000m);
            Profile(host.ForeignUnit, 7000m);
            Profile(host.ForeignUnit, 3000m);
            Profile(null, 9000m);
            // A pending request that narrows the grade to 4400..4600: it would leave every profile but the 4500 ones outside.
            db.Add(new PayrollReferenceChangeRequest
            {
                Id = request,
                TenantId = Tenant,
                EntityType = PayrollReferenceEntityType.PayGrade,
                Operation = PayrollReferenceOperation.Update,
                EntityId = grade,
                EntityLabel = "G53",
                CurrentJson = PayrollReferenceChangeJson.Write(new PayGradeChange(scale.Id, "G53", "درجة", 4500m, 4000m, 6000m)),
                ProposedJson = PayrollReferenceChangeJson.Write(new PayGradeChange(scale.Id, "G53", "درجة", 4500m, 4400m, 4600m)),
                Reason = "تضييق",
                LegalReference = "القرار 9 لعام 2026",
                State = PayrollReferenceChangeState.PendingApproval,
                RequestedBy = SomeoneElse,
                RequestedAt = DateTime.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

        const string naming = "ملفات أجر فعّالة على هذه الدرجة";
        const string outside = "منها خارج حدود الدرجة";
        var mine = await restricted.GetStringAsync($"/PayGrades/Details/{grade:D}");
        var everyones = await central.GetStringAsync($"/PayGrades/Details/{grade:D}");

        // The branch user's numbers are his branch's; the central user's are the agency's.
        Assert.Equal((2, 1), (CountAfter(mine, naming), CountAfter(mine, outside)));
        Assert.Equal((7, 4), (CountAfter(everyones, naming), CountAfter(everyones, outside)));

        // The same on the change request's page: how many active profiles the proposed bounds would leave outside.
        var myRequest = await restricted.GetStringAsync($"/PayrollReferenceChanges/Details/{request:D}");
        var theirRequest = await central.GetStringAsync($"/PayrollReferenceChanges/Details/{request:D}");
        var count = new System.Text.RegularExpressions.Regex(@"data-hr-outside-proposed=""(\d+)""");
        Assert.Equal("1", count.Match(myRequest).Groups[1].Value);
        Assert.Equal("5", count.Match(theirRequest).Groups[1].Value);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Every_write_of_the_salary_change_api_needs_the_antiforgery_token_and_without_it_nothing_is_written()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        using var central = host.Central();
        using var centralApprover = host.CentralAs(ApproverRoles);
        using var other = await WithApiTokenAsync(host.Restricted());
        var (ownRequest, _) = host.Records[typeof(PayProfileChangeRequest)];
        var employee = await AddEmployeeAsync(host, host.OwnUnit);
        var before = await RequestCountAsync(host);

        // No token: every POST is refused (400) before it reaches its handler, for a user who holds the act's role.
        var submit = await central.PostAsJsonAsync("/api/payroll/pay-profile-changes", Body(employee));
        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);
        foreach (var (by, action) in new[] { (centralApprover, "approve"), (centralApprover, "reject"), (central, "cancel") })
        {
            var refused = await by.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{ownRequest}/{action}", new { note = "ملاحظة", reason = "سبب" });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await centralApprover.PostAsync("/api/payroll/pay-profile-changes/activate-due", null)).StatusCode);
        Assert.Equal(before, await RequestCountAsync(host));
        Assert.Equal(PayProfileChangeState.PendingApproval, await StateAsync(host, ownRequest));

        // Another user's token is not this user's: the same client with it is refused too.
        string header, foreignToken;
        using (var issued = await other.GetAsync("/api/payroll/pay-profile-changes/antiforgery"))
        using (var token = JsonDocument.Parse(await issued.Content.ReadAsStringAsync()))
        {
            header = token.RootElement.GetProperty("headerName").GetString()!;
            foreignToken = token.RootElement.GetProperty("requestToken").GetString()!;
        }

        using (var forged = new HttpRequestMessage(HttpMethod.Post, $"/api/payroll/pay-profile-changes/{ownRequest}/approve"))
        {
            forged.Headers.Add(header, foreignToken);
            forged.Content = JsonContent.Create(new { note = "ملاحظة" });
            Assert.Equal(HttpStatusCode.BadRequest, (await centralApprover.SendAsync(forged)).StatusCode);
        }

        Assert.Equal(PayProfileChangeState.PendingApproval, await StateAsync(host, ownRequest));

        // Reads need no token; and with his own token the same user's write goes through.
        Assert.Equal(HttpStatusCode.OK, (await central.GetAsync($"/api/payroll/pay-profile-changes/{ownRequest}")).StatusCode);
        await WithApiTokenAsync(central);
        var accepted = await central.PostAsJsonAsync("/api/payroll/pay-profile-changes", Body(employee));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(before + 1, await RequestCountAsync(host));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_request_form_opens_and_a_refused_form_post_shows_the_message_with_the_apis_status()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        using var central = host.Central();
        var employee = await AddEmployeeAsync(host, host.OwnUnit);
        var before = await RequestCountAsync(host);

        // The form renders under the service's Arabic culture (its numeric limits once failed to parse there).
        var form = await central.GetAsync($"/EmployeePayProfiles/Create?employeeId={employee}");
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var html = await form.Content.ReadAsStringAsync();
        Assert.Contains("طلب تغيير راتب أو درجة", html, StringComparison.Ordinal);
        // A form with nothing to report writes no validation summary: an empty one carries an inline style attribute
        // (<li style="display:none">), which the enforced Content-Security-Policy refuses (found by the server's browser check).
        Assert.DoesNotContain("style=\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("validation-summary-valid", html, StringComparison.Ordinal);
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEqual("", token);

        async Task<HttpResponseMessage> PostAsync(string effectiveFrom, string salary, string reason) =>
            await central.PostAsync("/EmployeePayProfiles/Create", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["EmployeeId"] = employee.ToString("D"),
                ["ProposedBaseSalary"] = salary,
                ["EffectiveFrom"] = effectiveFrom,
                ["Reason"] = reason,
                ["LegalReference"] = "القرار 112 لعام 2026",
            }));

        // A broken rule: 400, the form again with the Arabic message, and no row.
        var midMonth = await PostAsync(NextMonth().AddDays(4).ToString("yyyy-MM-dd"), "5100", "ترفيع");
        Assert.Equal(HttpStatusCode.BadRequest, midMonth.StatusCode);
        Assert.Contains("تاريخ السريان يجب أن يكون أول يوم من شهر", WebUtility.HtmlDecode(await midMonth.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(NextMonth().ToString("yyyy-MM-dd"), "5100", "")).StatusCode);
        Assert.Equal(before, await RequestCountAsync(host));

        // A good request: saved as a request, and the caller lands on its details. The pay profile is untouched.
        var created = await PostAsync(NextMonth().ToString("yyyy-MM-dd"), "5100", "ترفيع استثنائي");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Contains("/PayProfileChanges/Details/", created.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal(before + 1, await RequestCountAsync(host));
        var profile = await host.WithDbAsync(db => db.Set<EmployeePayProfile>().AsNoTracking().SingleAsync(p => p.EmployeeId == employee));
        Assert.Equal((4500m, true), (profile.BaseSalary, profile.IsActive));
        var details = await central.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);

        // A second open request of the same employee: 409 on the form too.
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(NextMonth().ToString("yyyy-MM-dd"), "5200", "ترفيع آخر")).StatusCode);
        Assert.Equal(before + 1, await RequestCountAsync(host));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_module_role_alone_and_the_wrong_operational_role_are_refused_before_anything_is_read()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var (ownRequest, _) = host.Records[typeof(PayProfileChangeRequest)];
        var (ownEmployee, _) = host.Records[typeof(EmployeePayProfile)];
        using var moduleOnly = await WithApiTokenAsync(host.CentralWith(HrManagementRoles.Payroll, Guid.NewGuid()));
        using var approverOnly = await WithApiTokenAsync(host.CentralWith($"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}", Guid.NewGuid()));
        using var processorOnly = await WithApiTokenAsync(host.CentralWith($"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor}", Guid.NewGuid()));
        var before = await RequestCountAsync(host);

        // `payroll` alone reads the list and writes nothing.
        Assert.Equal(HttpStatusCode.OK, (await moduleOnly.GetAsync("/api/payroll/pay-profile-changes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await moduleOnly.PostAsJsonAsync("/api/payroll/pay-profile-changes", Body(ownEmployee))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await moduleOnly.GetAsync("/EmployeePayProfiles/Create")).StatusCode);
        foreach (var action in new[] { "approve", "reject", "cancel" })
            Assert.Equal(HttpStatusCode.Forbidden, (await moduleOnly.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{ownRequest}/{action}", new { note = "x", reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await moduleOnly.PostAsync("/api/payroll/pay-profile-changes/activate-due", null)).StatusCode);

        // An approver does not request (the form, the API, the alias); a processor does not decide.
        Assert.Equal(HttpStatusCode.Forbidden, (await approverOnly.PostAsJsonAsync("/api/payroll/pay-profile-changes", Body(ownEmployee))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approverOnly.PutAsJsonAsync($"/api/payroll/employees/{ownEmployee}/pay-profile",
            new { baseSalary = 5100m, effectiveFrom = DateTime.UtcNow, changeReason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approverOnly.GetAsync("/EmployeePayProfiles/Create")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await processorOnly.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{ownRequest}/approve", new { note = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await processorOnly.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{ownRequest}/reject", new { note = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await processorOnly.PostAsync("/api/payroll/pay-profile-changes/activate-due", null)).StatusCode);
        // A processor who is not the requester cannot cancel somebody else's request (the handler's rule, not a policy).
        var cancel = await processorOnly.PostAsJsonAsync($"/api/payroll/pay-profile-changes/{ownRequest}/cancel", new { reason = "سبب" });
        Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);

        Assert.Equal(before, await RequestCountAsync(host));
        Assert.Equal(PayProfileChangeState.PendingApproval, await StateAsync(host, ownRequest));
    }
}
