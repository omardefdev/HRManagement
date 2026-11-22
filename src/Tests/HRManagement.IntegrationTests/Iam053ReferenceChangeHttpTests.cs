using System.Net;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.OrgScope;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-53 (§4, D-53-4) over HTTP on Payroll.Web itself (PostgreSQL), through the pages a user opens: a processor requests a higher
/// maximum for a grade with a legal reference; the grade keeps its approved value until a different approver approves; the request
/// keeps who asked, who approved and the reference; nobody decides his own request; a grade pay profiles name is not deleted; and the
/// module role alone changes nothing. Every new page is rendered here under the service's Arabic culture.
/// </summary>
[Collection("PostgreSql")]
public sealed class Iam053ReferenceChangeHttpTests(PostgreSqlFixture postgres)
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    private static async Task<Dictionary<Type, Guid>> SeedAsync(PayrollDbContext db, Guid unit)
    {
        var employee = Guid.NewGuid();
        var scale = new SalaryScale { TenantId = Tenant, Code = $"S-{unit:N}"[..12], NameAr = "سلم الفرع", EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var used = new PayGrade { TenantId = Tenant, SalaryScale = scale, GradeCode = "G5", GradeNameAr = "الخامسة", BaseSalary = 4500m, MinSalary = 4000m, MaxSalary = 6000m };
        var free = new PayGrade { TenantId = Tenant, SalaryScale = scale, GradeCode = "G6", GradeNameAr = "السادسة", BaseSalary = 6500m, MinSalary = 6000m, MaxSalary = 8000m };
        db.AddRange(scale, used, free);
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            Id = employee,
            TenantId = Tenant,
            EmployeeId = employee,
            PayGrade = used,
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
            ProposedBaseSalary = 5000m,
            EffectiveFrom = new DateOnly(2099, 1, 1),
            Reason = "ترفيع",
            State = PayProfileChangeState.Rejected,
            RequestedBy = Guid.NewGuid(),
            RequestedAt = DateTime.UtcNow,
        };
        db.PayProfileChangeRequests.Add(request);
        await db.SaveChangesAsync();
        return new Dictionary<Type, Guid> { [typeof(EmployeePayProfile)] = employee, [typeof(PayProfileChangeRequest)] = request.Id };
    }

    private Task<Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>> StartAsync() =>
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>.StartAsync(
            postgres, "PayrollDb", PayrollOrgScopeKeys.Registry,
            $"{HrManagementRoles.BranchManager},{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}",
            $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor}",
            "/EmployeePayProfiles/Create", SeedAsync);

    /// <summary>An amount as a page prints it ("N2"), whatever separators the request's culture uses between the digit groups.</summary>
    private static void AssertShowsAmount(string html, string thousands, string hundreds)
    {
        Assert.True(Regex.IsMatch(html, $"(?<![0-9]){thousands}[^0-9<]{{0,3}}{hundreds}[^0-9<]{{0,3}}00(?![0-9])"),
            $"The page does not show the amount {thousands}{hundreds}.00");
    }

    private static async Task<string> PageAsync(HttpClient client, string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.GetAsync(url);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.True(response.StatusCode == expected, $"GET {url}: {(int)response.StatusCode}, expected {(int)expected}\n{html[..Math.Min(html.Length, 600)]}");
        return html;
    }

    /// <summary>Opens the form, reads its token, and posts it with the fields.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string formUrl, string postUrl, Dictionary<string, string> fields)
    {
        var match = Token.Match(await PageAsync(client, formUrl));
        Assert.True(match.Success, $"No antiforgery token on {formUrl}");
        fields["__RequestVerificationToken"] = match.Groups[1].Value;
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    private static Dictionary<string, string> GradeFields(Guid scale, string max, string reason = "رفع الحد الأعلى بمرسوم الزيادة", string reference = "المرسوم 7 لعام 2026") => new()
    {
        ["SalaryScaleId"] = scale.ToString("D"),
        ["GradeCode"] = "G5",
        ["GradeNameAr"] = "الخامسة",
        ["BaseSalary"] = "4500",
        ["MinSalary"] = "4000",
        ["MaxSalary"] = max,
        ["Reason"] = reason,
        ["LegalReference"] = reference,
    };

    private static Guid RequestIdOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        var match = Regex.Match(location, "/PayrollReferenceChanges/Details/([0-9a-fA-F-]{36})");
        Assert.True(match.Success, $"Not redirected to the request: {location}");
        return Guid.Parse(match.Groups[1].Value);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_grades_maximum_is_raised_by_a_request_another_person_approves_and_a_grade_in_use_is_not_deleted()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        // Two different people: the processor (the host's central user) and the approver (the host's restricted user).
        using var processor = host.Central();
        using var approver = host.Restricted();
        var (grade, scale, free) = await host.WithDbAsync(async db =>
        {
            var g = await db.Set<PayGrade>().AsNoTracking().Where(x => x.GradeCode == "G5").OrderBy(x => x.Id).FirstAsync();
            var f = await db.Set<PayGrade>().AsNoTracking().FirstAsync(x => x.SalaryScaleId == g.SalaryScaleId && x.GradeCode == "G6");
            return (g.Id, g.SalaryScaleId, f.Id);
        });
        Task<decimal> MaxAsync() => host.WithDbAsync(db => db.Set<PayGrade>().AsNoTracking().Where(g => g.Id == grade).Select(g => g.MaxSalary).SingleAsync());

        // Every page of the two controllers renders for the processor.
        foreach (var url in new[]
                 {
                     "/PayGrades", $"/PayGrades/Details/{grade}", "/PayGrades/Create", $"/PayGrades/Edit/{grade}", $"/PayGrades/Delete/{free}",
                     "/SalaryScales", $"/SalaryScales/Details/{scale}", "/SalaryScales/Create", $"/SalaryScales/Edit/{scale}", $"/SalaryScales/Delete/{scale}",
                     "/PayrollReferenceChanges", "/EmployeePayProfiles?outsideBounds=true",
                 })
            await PageAsync(processor, url);

        // The processor requests a maximum of 7500 with a legal reference.
        var submitted = await PostFormAsync(processor, $"/PayGrades/Edit/{grade}", $"/PayGrades/Edit/{grade}", GradeFields(scale, "7500"));
        var requestId = RequestIdOf(submitted);
        Assert.Equal(6000m, await MaxAsync());                                       // before the approval the old maximum stands
        AssertShowsAmount(await PageAsync(processor, $"/PayGrades/Details/{grade}"), "6", "000");
        Assert.Contains("بانتظار الاعتماد", await PageAsync(processor, $"/PayGrades/Details/{grade}"), StringComparison.Ordinal);

        // The request's page: a readable before and after with Arabic field names; the JSON only under the technical view.
        var details = await PageAsync(processor, $"/PayrollReferenceChanges/Details/{requestId}");
        Assert.Contains("الحد الأعلى", details, StringComparison.Ordinal);
        AssertShowsAmount(details, "7", "500");
        Assert.Contains("المرسوم 7 لعام 2026", details, StringComparison.Ordinal);
        Assert.Contains("عرض تقني", details, StringComparison.Ordinal);
        // The processor holds no approval policy: no decision form on his page, and a crafted decision is refused.
        Assert.DoesNotContain("/PayrollReferenceChanges/Approve/", details, StringComparison.Ordinal);
        var crafted = await PostFormAsync(processor, $"/PayGrades/Edit/{free}", $"/PayrollReferenceChanges/Approve/{requestId}", new() { ["note"] = "" });
        Assert.Equal(HttpStatusCode.Forbidden, crafted.StatusCode);
        Assert.Equal(6000m, await MaxAsync());

        // A second request for the same grade while one is open: 409, the form again with the message.
        var second = await PostFormAsync(processor, $"/PayGrades/Edit/{free}", $"/PayGrades/Edit/{grade}", GradeFields(scale, "7000"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("للسجل طلب تغيير مفتوح", WebUtility.HtmlDecode(await second.Content.ReadAsStringAsync()), StringComparison.Ordinal);

        // The approver, another person, approves from the request's page.
        var approverPage = await PageAsync(approver, $"/PayrollReferenceChanges/Details/{requestId}");
        Assert.Contains($"/PayrollReferenceChanges/Approve/{requestId}", approverPage, StringComparison.Ordinal);
        var approved = await approver.PostAsync($"/PayrollReferenceChanges/Approve/{requestId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token.Match(approverPage).Groups[1].Value,
            ["note"] = "موافق",
        }));
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        Assert.Equal(7500m, await MaxAsync());
        AssertShowsAmount(await PageAsync(processor, $"/PayGrades/Details/{grade}"), "7", "500");

        // The request keeps the requester, the approver and the reference.
        var stored = await host.WithDbAsync(db => db.Set<PayrollReferenceChangeRequest>().AsNoTracking().SingleAsync(r => r.Id == requestId));
        Assert.Equal(PayrollReferenceChangeState.Applied, stored.State);
        Assert.NotEqual(stored.RequestedBy, stored.DecidedBy);
        Assert.NotNull(stored.DecidedBy);
        Assert.Equal("المرسوم 7 لعام 2026", stored.LegalReference);
        Assert.Contains("اعتماد وتطبيق", await PageAsync(approver, $"/PayrollReferenceChanges/Details/{requestId}"), StringComparison.Ordinal);

        // A fractional amount, as a numeric input sends it (a dot), is read as it is under the service's Arabic culture.
        var fractional = await PostFormAsync(processor, $"/PayGrades/Edit/{grade}", $"/PayGrades/Edit/{grade}", GradeFields(scale, "8250.75"));
        var fractionalId = RequestIdOf(fractional);
        var proposed = await host.WithDbAsync(db => db.Set<PayrollReferenceChangeRequest>().AsNoTracking().Where(r => r.Id == fractionalId).Select(r => r.ProposedJson).SingleAsync());
        Assert.Contains("8250.75", proposed!, StringComparison.Ordinal);
        var cancelPage = await PageAsync(processor, $"/PayrollReferenceChanges/Details/{fractionalId}");
        var cancelFractional = await processor.PostAsync($"/PayrollReferenceChanges/Cancel/{fractionalId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token.Match(cancelPage).Groups[1].Value,
            ["reason"] = "تجربة الكسر العشري",
        }));
        Assert.Equal(HttpStatusCode.Redirect, cancelFractional.StatusCode);

        // A broken proposal: 400 and the message (the minimum above the base salary).
        var broken = GradeFields(scale, "9000");
        broken["MinSalary"] = "5000";
        var refused = await PostFormAsync(processor, $"/PayGrades/Edit/{grade}", $"/PayGrades/Edit/{grade}", broken);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("الحد الأدنى ≤ الراتب الأساسي ≤ الحد الأعلى", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()), StringComparison.Ordinal);

        // Deleting the grade the pay profile names: refused with the message, and the grade stays.
        var delete = await PostFormAsync(processor, $"/PayGrades/Delete/{grade}", $"/PayGrades/Delete/{grade}",
            new() { ["Reason"] = "لم تعد مستعملة", ["LegalReference"] = "القرار 3 لعام 2026" });
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Contains("لا تُحذف الدرجة", WebUtility.HtmlDecode(await delete.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(7500m, await MaxAsync());
        // And a scale that has grades.
        var deleteScale = await PostFormAsync(processor, $"/SalaryScales/Delete/{scale}", $"/SalaryScales/Delete/{scale}",
            new() { ["Reason"] = "ألغي السلّم", ["LegalReference"] = "القرار 4 لعام 2026" });
        Assert.Equal(HttpStatusCode.Conflict, deleteScale.StatusCode);
        Assert.Equal(1, await host.WithDbAsync(db => db.Set<SalaryScale>().CountAsync(s => s.Id == scale)));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Nobody_decides_his_own_reference_request_and_the_module_role_alone_requests_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var (grade, scale) = await host.WithDbAsync(async db =>
        {
            var g = await db.Set<PayGrade>().AsNoTracking().Where(x => x.GradeCode == "G5").OrderBy(x => x.Id).FirstAsync();
            return (g.Id, g.SalaryScaleId);
        });

        // hr-admin holds Payroll.MasterData and Payroll.Approve: he requests, and cannot approve or reject his own request.
        using var admin = host.CentralWith(HrManagementRoles.Admin, Guid.NewGuid());
        var requestId = RequestIdOf(await PostFormAsync(admin, $"/PayGrades/Edit/{grade}", $"/PayGrades/Edit/{grade}", GradeFields(scale, "7500")));
        var page = await PageAsync(admin, $"/PayrollReferenceChanges/Details/{requestId}");
        Assert.Contains("لا يجوز اعتماد الطلب من طالبه", page, StringComparison.Ordinal);
        Assert.DoesNotContain($"/PayrollReferenceChanges/Approve/{requestId}", page, StringComparison.Ordinal);
        foreach (var action in new[] { "Approve", "Reject" })
        {
            var own = await admin.PostAsync($"/PayrollReferenceChanges/{action}/{requestId}", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Token.Match(page).Groups[1].Value,
                ["note"] = "ملاحظة",
            }));
            Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
            Assert.Contains("لا يجوز اعتماد الطلب من طالبه", await own.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(PayrollReferenceChangeState.PendingApproval,
            await host.WithDbAsync(db => db.Set<PayrollReferenceChangeRequest>().AsNoTracking().Where(r => r.Id == requestId).Select(r => r.State).SingleAsync()));
        Assert.Equal(6000m, await host.WithDbAsync(db => db.Set<PayGrade>().AsNoTracking().Where(g => g.Id == grade).Select(g => g.MaxSalary).SingleAsync()));

        // He may cancel his own request.
        var cancelled = await admin.PostAsync($"/PayrollReferenceChanges/Cancel/{requestId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token.Match(page).Groups[1].Value,
            ["reason"] = "أُدخل الرقم خطأ",
        }));
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);

        // The module role alone: the lists open, the request forms and the posts do not.
        using var moduleOnly = host.RestrictedWith(HrManagementRoles.Payroll, host.OwnUnit, Guid.NewGuid());
        await PageAsync(moduleOnly, "/PayGrades");
        await PageAsync(moduleOnly, "/SalaryScales");
        await PageAsync(moduleOnly, "/PayrollReferenceChanges");
        foreach (var url in new[] { "/PayGrades/Create", $"/PayGrades/Edit/{grade}", $"/PayGrades/Delete/{grade}", "/SalaryScales/Create", $"/SalaryScales/Edit/{scale}", $"/SalaryScales/Delete/{scale}" })
            await PageAsync(moduleOnly, url, HttpStatusCode.Forbidden);
        var before = await host.WithDbAsync(db => db.Set<PayrollReferenceChangeRequest>().CountAsync());
        var token = Token.Match(page).Groups[1].Value;
        foreach (var url in new[] { $"/PayGrades/Edit/{grade}", "/PayGrades/Create", $"/PayGrades/Delete/{grade}", $"/SalaryScales/Edit/{scale}", "/SalaryScales/Create", $"/SalaryScales/Delete/{scale}" })
        {
            var fields = GradeFields(scale, "9900");
            fields["__RequestVerificationToken"] = token;
            Assert.Equal(HttpStatusCode.Forbidden, (await moduleOnly.PostAsync(url, new FormUrlEncodedContent(fields))).StatusCode);
        }

        Assert.Equal(before, await host.WithDbAsync(db => db.Set<PayrollReferenceChangeRequest>().CountAsync()));
    }
}
