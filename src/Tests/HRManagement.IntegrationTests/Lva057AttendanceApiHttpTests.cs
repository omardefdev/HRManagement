using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-57: HR.Core.Web over a database of its own, with an agency of two branches (branch 1 has a sub-unit).</summary>
public sealed class Lva057HrCoreApiHost : IAsyncLifetime
{
    public static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    public static readonly Guid BranchManager = Guid.Parse("57000000-0000-4000-8000-0000000000c1");
    public static readonly Guid CentralOne = Guid.Parse("57000000-0000-4000-8000-0000000000c2");
    public static readonly Guid CentralTwo = Guid.Parse("57000000-0000-4000-8000-0000000000c3");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hr-lva057-core-{Guid.NewGuid():N}.db");

    public WebApplicationFactory<HRCoreWebProgram> Host { get; private set; } = null!;

    public Guid Hq { get; private set; }

    public Guid Branch1 { get; private set; }

    public Guid Branch1Sub { get; private set; }

    public Guid Branch2 { get; private set; }

    public async Task InitializeAsync()
    {
        Host = new WebApplicationFactory<HRCoreWebProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recruitment:Jobs:IntakeCloseEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:OfferExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:ReserveExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateNotificationsEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateRetentionEnabled", "false");
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:HRCoreDb", $"Data Source={_dbPath}");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:RunMigrationsAtStartup"] = "false",
                ["ConnectionStrings:HRCoreDb"] = $"Data Source={_dbPath}",
                ["HrAuth:BehindGateway"] = "true",
                ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                ["HrGateway:InternalBaseUrl"] = "http://localhost:5200",
                ["NationalScope:IsLabScale"] = "false",
                // The holidays of the test's February, as the acceptance layer sets its own.
                ["Attendance:Calendar:Holidays:0"] = "2025-02-17"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddIntegrationTestAuthentication();
            });
        });

        await WithDbAsync(async db =>
        {
            await db.Database.EnsureCreatedAsync();
            await LegislationRegisterSeeder.SeedAsync(db);
            var hq = Unit("HQ", null, null);
            var b1 = Unit("B1", hq.Id, hq.MaterializedPath);
            var b1Sub = Unit("B1S", b1.Id, b1.MaterializedPath);
            var b2 = Unit("B2", hq.Id, hq.MaterializedPath);
            db.OrgUnits.AddRange(hq, b1, b1Sub, b2);
            await db.SaveChangesAsync();
            (Hq, Branch1, Branch1Sub, Branch2) = (hq.Id, b1.Id, b1Sub.Id, b2.Id);
            return true;
        });
    }

    private static OrgUnit Unit(string code, Guid? parent, string? parentPath)
    {
        var unit = new OrgUnit { TenantId = Tenant, Code = $"{code}-57-{Guid.NewGuid():N}"[..14], NameAr = code, NameEn = code, ParentId = parent };
        unit.MaterializedPath = $"{parentPath ?? "/"}{unit.Id:D}/";
        return unit;
    }

    public async Task<T> WithDbAsync<T>(Func<HRCoreDbContext, Task<T>> work)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        TenantIdFlow.Set(Tenant);
        try
        {
            return await work(scope.ServiceProvider.GetRequiredService<HRCoreDbContext>());
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    /// <summary>A new unit under the agency root, for a test that needs a tree nobody else writes.</summary>
    public async Task<Guid> NewUnitAsync(Guid? parent = null)
    {
        return await WithDbAsync(async db =>
        {
            var parentUnit = await db.OrgUnits.AsNoTracking().FirstAsync(o => o.Id == (parent ?? Hq));
            var unit = Unit("T", parentUnit.Id, parentUnit.MaterializedPath);
            db.OrgUnits.Add(unit);
            await db.SaveChangesAsync();
            // The org scope caches a branch's tree (as the product does, and invalidates it when a unit is written through its screens).
            Host.Services.GetRequiredService<HR.Core.Infrastructure.IOrgUnitHierarchyCache>().InvalidateAll();
            return unit.Id;
        });
    }

    public async Task<Guid> EmployeeAsync(Guid unit, string name = "موظف واجهة")
    {
        return await WithDbAsync(async db =>
        {
            var id = Guid.NewGuid();
            db.Employees.Add(new Employee
            {
                Id = id,
                TenantId = Tenant,
                FirstName = name,
                LastName = "الحضور",
                FullName = name + " الحضور",
                NationalId = "N" + id.ToString("N")[..10],
                BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Gender = Gender.Male,
                CurrentStatus = EmploymentStatus.Active,
                PrimaryOrgUnitId = unit,
                HireDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
            return id;
        });
    }

    public HttpClient Client(Guid user, string roles, Guid? scope = null)
    {
        var client = Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Tenant.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        if (scope is Guid unit)
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeOrgUnitHeader, unit.ToString("D"));
        return client;
    }

    /// <summary>The manager of a branch: hr-core, branch-scoped to <paramref name="unit"/> (branch 1 by default).</summary>
    public HttpClient Branch(Guid? unit = null, Guid? user = null) =>
        Client(user ?? BranchManager, $"{HrManagementRoles.BranchManager},{HrManagementRoles.HrCore}", unit ?? Branch1);

    public HttpClient Central(Guid? user = null) => Client(user ?? CentralOne, HrManagementRoles.HrCore);

    /// <summary>A supervisory employee: he reads HR.Core (<c>HrCore.ReadOnly</c>) and writes nothing.</summary>
    public HttpClient Reader() => Client(Guid.NewGuid(), $"{HrManagementRoles.HrEmployee},{HrManagementRoles.HrCoreSupervisory}");

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            System.IO.File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // The file is a temporary one; a handle still open on it is not this test's failure.
        }
    }
}

/// <summary>
/// PLAN-57 (P-4): the attendance API and screens over HTTP, on HR.Core.Web with its real pipeline (policies, the org-scope filter,
/// antiforgery, model binding): the statuses of a close and of a correction, a token on every new write, the three policies by role
/// and scope, and that an edit cannot move a record.
/// </summary>
public sealed class Lva057AttendanceApiHttpTests(Lva057HrCoreApiHost host) : IClassFixture<Lva057HrCoreApiHost>
{
    private const string Reason = "كان في مهمة رسمية موثقة بكتاب الإدارة";
    private static readonly Regex FormToken = new("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>The API's token: the cookie stays in the client, the request token goes in its header.</summary>
    private static async Task<HttpClient> WithApiTokenAsync(HttpClient client, string path)
    {
        var issued = await client.GetFromJsonAsync<JsonElement>(path);
        client.DefaultRequestHeaders.Add(issued.GetProperty("headerName").GetString()!, issued.GetProperty("requestToken").GetString());
        return client;
    }

    private static Task<HttpClient> ForPeriodsAsync(HttpClient client) => WithApiTokenAsync(client, "/api/attendance/periods/antiforgery");

    /// <summary>The corrections' own token endpoint; a token of one endpoint of the service is accepted by its other API writes.</summary>
    private static Task<HttpClient> ForCorrectionsAsync(HttpClient client) => WithApiTokenAsync(client, "/api/attendance/corrections/antiforgery");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return text.TrimStart().StartsWith('{') ? JsonSerializer.Deserialize<JsonElement>(text) : default;
    }

    /// <summary>Posts a screen's form with the token the page renders.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string page, string action, params (string Key, string Value)[] fields)
    {
        var html = await (await client.GetAsync(page)).Content.ReadAsStringAsync();
        var token = WebUtility.HtmlDecode(FormToken.Match(html).Groups[1].Value);
        Assert.False(string.IsNullOrEmpty(token), $"{page} rendered no antiforgery token");
        var form = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).Append(new("__RequestVerificationToken", token));
        return await client.PostAsync(action, new FormUrlEncodedContent(form));
    }

    private async Task FillAsync(Guid unit, string month)
    {
        using var central = host.Central();
        using var filled = await PostFormAsync(central, "/DailyAttendanceRecords/BulkEntry", "/DailyAttendanceRecords/BulkEntry",
            ("orgUnitId", unit.ToString("D")), ("month", month), ("defaultStatus", "0"));
        Assert.Equal(HttpStatusCode.Redirect, filled.StatusCode);
    }

    private Task<DailyAttendanceRecord?> RecordAsync(Guid employee, DateOnly day) =>
        host.WithDbAsync(db => db.DailyAttendanceRecords.AsNoTracking().FirstOrDefaultAsync(r => r.EmployeeId == employee && r.AttendanceDate == day));

    // ---------------------------------------------------------------- the close

    [Fact]
    public async Task The_close_answers_409_with_who_is_missing_then_200_and_a_repeat_writes_nothing()
    {
        var unit = await host.NewUnitAsync(host.Branch1);
        var first = await host.EmployeeAsync(unit);
        var second = await host.EmployeeAsync(unit);
        using var branch = await ForPeriodsAsync(host.Branch());

        // No record yet: RequireComplete refuses the whole close with the list, and writes nothing.
        using var incomplete = await branch.PostAsJsonAsync("/api/attendance/periods/2025-03/close", new { orgUnitId = unit });
        Assert.Equal(HttpStatusCode.Conflict, incomplete.StatusCode);
        var refused = await JsonAsync(incomplete);
        Assert.Equal(2, refused.GetProperty("total").GetInt32());
        // March 2025 has 22 working days; each employee misses all of them.
        Assert.Equal(new[] { first, second }.Order(), refused.GetProperty("incomplete").EnumerateArray().Select(i => i.GetProperty("employeeId").GetGuid()).Order());
        Assert.All(refused.GetProperty("incomplete").EnumerateArray(), i => Assert.Equal(22, i.GetProperty("unrecordedWorkingDays").GetInt32()));
        Assert.Equal(0, await host.WithDbAsync(db => db.AttendanceEmployeeSummaries.CountAsync(s => s.EmployeeId == first || s.EmployeeId == second)));

        await FillAsync(unit, "2025-03");
        using var closed = await branch.PostAsJsonAsync("/api/attendance/periods/2025-03/close", new { orgUnitId = unit });
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var done = await JsonAsync(closed);
        Assert.Equal((2, 0), (done.GetProperty("closedEmployees").GetInt32(), done.GetProperty("skippedAlreadyClosed").GetInt32()));
        Assert.NotEqual(Guid.Empty, done.GetProperty("periodId").GetGuid());
        Assert.Contains(done.GetProperty("status").GetString(), new[] { "Closed", "PartiallyClosed" });

        using var again = await branch.PostAsJsonAsync("/api/attendance/periods/2025-03/close", new { orgUnitId = unit });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var repeated = await JsonAsync(again);
        Assert.Equal((0, 2), (repeated.GetProperty("closedEmployees").GetInt32(), repeated.GetProperty("skippedAlreadyClosed").GetInt32()));
        Assert.Equal(1, await host.WithDbAsync(db => db.AttendancePeriodClosures.CountAsync(c => c.OrgUnitId == unit)));

        // The read: the branch sees the summaries of its own tree, each at version 1.
        var period = await branch.GetFromJsonAsync<JsonElement>("/api/attendance/periods/2025-03");
        var mine = period.GetProperty("summaries").EnumerateArray().Where(s => s.GetProperty("employeeId").GetGuid() == first || s.GetProperty("employeeId").GetGuid() == second).ToList();
        Assert.Equal(2, mine.Count);
        Assert.All(mine, s => Assert.Equal((1, 22, 22, 0), (s.GetProperty("version").GetInt32(), s.GetProperty("workingDays").GetInt32(), s.GetProperty("presentDays").GetInt32(), s.GetProperty("absentDays").GetInt32())));

        // Closed days are locked: a manual write on one of them is refused on the form, and nothing is written.
        using var locked = await PostFormAsync(branch, "/DailyAttendanceRecords/Create", "/DailyAttendanceRecords/Create",
            ("EmployeeId", first.ToString("D")), ("AttendanceDate", "2025-03-07"), ("Status", "Absent"), ("Notes", "محاولة على يوم مقفل"));
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("اليوم ضمن فترة حضور مغلقة", WebUtility.HtmlDecode(await locked.Content.ReadAsStringAsync()));
        Assert.Null(await RecordAsync(first, new DateOnly(2025, 3, 7)));
    }

    [Fact]
    public async Task The_close_answers_403_outside_the_scope_404_for_an_unknown_unit_and_400_for_a_month_it_cannot_close()
    {
        using var branch = await ForPeriodsAsync(host.Branch());
        using var central = await ForPeriodsAsync(host.Central());
        using var reader = host.Reader();

        async Task<HttpStatusCode> CloseAsync(HttpClient client, string month, object body)
        {
            using var response = await client.PostAsJsonAsync($"/api/attendance/periods/{month}/close", body);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Forbidden, await CloseAsync(branch, "2025-04", new { orgUnitId = host.Branch2 }));
        Assert.Equal(HttpStatusCode.Forbidden, await CloseAsync(branch, "2025-04", new { orgUnitId = (Guid?)null }));
        Assert.Equal(HttpStatusCode.Forbidden, await CloseAsync(branch, "2025-04", new { orgUnitId = host.Hq }));
        Assert.Equal(HttpStatusCode.NotFound, await CloseAsync(branch, "2025-04", new { orgUnitId = Guid.NewGuid() }));
        Assert.Equal(HttpStatusCode.NotFound, await CloseAsync(central, "2025-04", new { orgUnitId = Guid.NewGuid() }));
        Assert.Equal(HttpStatusCode.BadRequest, await CloseAsync(central, "2025-13", new { orgUnitId = host.Branch2 }));
        Assert.Equal(HttpStatusCode.BadRequest, await CloseAsync(central, "04-2025", new { orgUnitId = host.Branch2 }));
        // A month that has not ended is not closed.
        Assert.Equal(HttpStatusCode.BadRequest, await CloseAsync(central, "2099-01", new { orgUnitId = host.Branch2 }));
        Assert.Equal(0, await host.WithDbAsync(db => db.AttendancePeriods.CountAsync(p => p.PeriodStart == new DateOnly(2025, 4, 1))));

        // A reader holds none of the attendance policies: no token, no close, no correction.
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/attendance/periods/antiforgery")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await CloseAsync(reader, "2025-04", new { orgUnitId = host.Branch2 }));
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/attendance/corrections", new { })).StatusCode);
        // He reads a period's state (the class policy).
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/attendance/periods/2025-04")).StatusCode);
    }

    [Fact]
    public async Task Every_new_write_needs_an_antiforgery_token()
    {
        var unit = await host.NewUnitAsync(host.Branch1);
        using var noToken = host.Central();

        foreach (var (path, body) in new (string, object)[]
                 {
                     ("/api/attendance/periods/2025-05/close", new { orgUnitId = unit }),
                     ("/api/attendance/corrections", new { employeeId = Guid.NewGuid(), attendanceDate = "2025-05-05", newStatus = "Present", reason = Reason }),
                     ($"/api/attendance/corrections/{Guid.NewGuid():D}/approve", new { note = "x" }),
                     ($"/api/attendance/corrections/{Guid.NewGuid():D}/reject", new { note = "x" }),
                 })
        {
            using var response = await noToken.PostAsJsonAsync(path, body);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{path} without a token answered {(int)response.StatusCode}");
        }

        Assert.Equal(0, await host.WithDbAsync(db => db.AttendancePeriods.CountAsync(p => p.PeriodStart == new DateOnly(2025, 5, 1))));

        // The screens' forms too: a posted close with no token is refused.
        using var form = await noToken.PostAsync("/AttendancePeriods/Close", new FormUrlEncodedContent(
            [new("month", "2025-05"), new("orgUnitId", unit.ToString("D"))]));
        Assert.Equal(HttpStatusCode.BadRequest, form.StatusCode);
    }

    // ---------------------------------------------------------------- the correction

    [Fact]
    public async Task A_correction_is_asked_by_the_branch_and_decided_by_a_central_person_who_is_not_the_requester()
    {
        var unit = await host.NewUnitAsync(host.Branch1);
        var employee = await host.EmployeeAsync(unit);
        await FillAsync(unit, "2025-06");
        using var branch = await ForPeriodsAsync(host.Branch());
        using (var closed = await branch.PostAsJsonAsync("/api/attendance/periods/2025-06/close", new { orgUnitId = unit }))
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var day = new DateOnly(2025, 6, 3);

        async Task<(HttpStatusCode Status, JsonElement Body)> AskAsync(HttpClient client, object body)
        {
            using var response = await client.PostAsJsonAsync("/api/attendance/corrections", body);
            return (response.StatusCode, await JsonAsync(response));
        }

        // The request's rules: 400, and nothing written.
        Assert.Equal(HttpStatusCode.BadRequest, (await AskAsync(branch, new { employeeId = employee, attendanceDate = "2025-06-03", newStatus = "Absent", reason = "قصير" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await AskAsync(branch, new { employeeId = employee, attendanceDate = "2025-06-03", newStatus = "Present", reason = Reason })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await AskAsync(branch, new { employeeId = employee, attendanceDate = "2025-06-03", newStatus = "OnLeave", reason = Reason })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await AskAsync(branch, new { employeeId = employee, attendanceDate = "2025-06-03", newStatus = "Holiday", reason = Reason })).Status);
        // A body that is not a request — a date that is not a date, or no body at all — is 400 too, not a failure of the server.
        Assert.Equal(HttpStatusCode.BadRequest, (await AskAsync(branch, new { employeeId = employee, attendanceDate = "الثالث من حزيران", newStatus = "Absent", reason = Reason })).Status);
        using (var empty = await branch.PostAsync("/api/attendance/corrections", new StringContent("", System.Text.Encoding.UTF8, "application/json")))
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        // A day that is not locked (July is not closed) is edited directly, not corrected.
        Assert.Equal(HttpStatusCode.BadRequest, (await AskAsync(branch, new { employeeId = employee, attendanceDate = "2025-07-01", newStatus = "Absent", reason = Reason })).Status);
        Assert.Equal(0, await host.WithDbAsync(db => db.AttendanceCorrections.CountAsync(c => c.EmployeeId == employee)));

        var (asked, request) = await AskAsync(branch, new { employeeId = employee, attendanceDate = "2025-06-03", newStatus = "Absent", reason = Reason });
        Assert.Equal(HttpStatusCode.OK, asked);
        Assert.Equal("Pending", request.GetProperty("status").GetString());
        var id = request.GetProperty("id").GetGuid();
        // One request waits per employee and day.
        Assert.Equal(HttpStatusCode.Conflict, (await AskAsync(branch, new { employeeId = employee, attendanceDate = "2025-06-03", newStatus = "Remote", reason = Reason })).Status);

        // The branch never decides (the policy): 403, for an approval and a rejection alike.
        using (var byBranch = await branch.PostAsJsonAsync($"/api/attendance/corrections/{id:D}/approve", new { note = "أعتمد طلبي" }))
            Assert.Equal(HttpStatusCode.Forbidden, byBranch.StatusCode);
        using (var byBranch = await branch.PostAsJsonAsync($"/api/attendance/corrections/{id:D}/reject", new { note = "أرفض" }))
            Assert.Equal(HttpStatusCode.Forbidden, byBranch.StatusCode);

        using var central = await ForCorrectionsAsync(host.Central());
        using (var noNote = await central.PostAsJsonAsync($"/api/attendance/corrections/{id:D}/reject", new { note = " " }))
            Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);
        using (var unknown = await central.PostAsJsonAsync($"/api/attendance/corrections/{Guid.NewGuid():D}/approve", new { note = "x" }))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(DailyAttendanceStatus.Present, (await RecordAsync(employee, day))!.Status);

        using (var approved = await central.PostAsJsonAsync($"/api/attendance/corrections/{id:D}/approve", new { note = "مطابق" }))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var body = await JsonAsync(approved);
            Assert.Equal(("Approved", 2), (body.GetProperty("status").GetString(), body.GetProperty("resultingVersion").GetInt32()));
        }

        var record = await RecordAsync(employee, day);
        Assert.Equal((DailyAttendanceStatus.Absent, AttendanceRecordSource.Correction), (record!.Status, record.Source));
        Assert.Equal(2, await host.WithDbAsync(db => db.AttendanceEmployeeSummaries.Where(s => s.EmployeeId == employee).Select(s => s.Version).SingleAsync()));
        // Decided once: a second decision answers 409.
        using (var again = await central.PostAsJsonAsync($"/api/attendance/corrections/{id:D}/approve", new { note = "مرة ثانية" }))
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        using (var again = await central.PostAsJsonAsync($"/api/attendance/corrections/{id:D}/reject", new { note = "مرة ثانية" }))
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // A central person's own request: he holds the approval policy, and the server still refuses him (400); another central
        // person decides it.
        var (own, ownRequest) = await AskAsync(central, new { employeeId = employee, attendanceDate = "2025-06-04", newStatus = "Remote", reason = Reason });
        Assert.Equal(HttpStatusCode.OK, own);
        var ownId = ownRequest.GetProperty("id").GetGuid();
        using (var self = await central.PostAsJsonAsync($"/api/attendance/corrections/{ownId:D}/approve", new { note = "أعتمد طلبي" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
            Assert.Contains("لا يبتّ مقدّم الطلب", (await JsonAsync(self)).GetProperty("error").GetString());
        }

        Assert.Equal(DailyAttendanceStatus.Present, (await RecordAsync(employee, new DateOnly(2025, 6, 4)))!.Status);
        using var other = await ForCorrectionsAsync(host.Central(Lva057HrCoreApiHost.CentralTwo));
        using (var rejected = await other.PostAsJsonAsync($"/api/attendance/corrections/{ownId:D}/reject", new { note = "لا مستند" }))
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(2, await host.WithDbAsync(db => db.AttendanceEmployeeSummaries.Where(s => s.EmployeeId == employee).Select(s => s.Version).SingleAsync()));
    }

    [Fact]
    public async Task A_branch_asks_only_for_its_own_employees_and_sees_only_its_own_corrections()
    {
        var foreignUnit = await host.NewUnitAsync(host.Branch2);
        var foreign = await host.EmployeeAsync(foreignUnit);
        await FillAsync(foreignUnit, "2025-06");
        using var central = await ForPeriodsAsync(host.Central());
        using (var closed = await central.PostAsJsonAsync("/api/attendance/periods/2025-06/close", new { orgUnitId = foreignUnit }))
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Guid foreignRequest;
        using (var asked = await central.PostAsJsonAsync("/api/attendance/corrections",
                   new { employeeId = foreign, attendanceDate = "2025-06-03", newStatus = "Absent", reason = Reason }))
        {
            Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
            foreignRequest = (await JsonAsync(asked)).GetProperty("id").GetGuid();
        }

        using var branch = await ForCorrectionsAsync(host.Branch());
        // The other branch's employee is not his: the org-scope filter answers before the service (404), and nothing is written.
        using (var asked = await branch.PostAsJsonAsync("/api/attendance/corrections",
                   new { employeeId = foreign, attendanceDate = "2025-06-04", newStatus = "Absent", reason = Reason }))
            Assert.Contains(asked.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden });
        Assert.Equal(1, await host.WithDbAsync(db => db.AttendanceCorrections.CountAsync(c => c.EmployeeId == foreign)));
        // The other branch's request is not a page of his, and his list does not show it.
        Assert.Equal(HttpStatusCode.NotFound, (await branch.GetAsync($"/AttendanceCorrections/Details/{foreignRequest:D}")).StatusCode);
        var list = await (await branch.GetAsync("/AttendanceCorrections")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(foreignRequest.ToString("D"), list);
        Assert.Contains(foreignRequest.ToString("D"), await (await central.GetAsync("/AttendanceCorrections")).Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- the screens

    [Fact]
    public async Task An_edit_changes_the_status_and_ignores_a_posted_employee_and_date()
    {
        var unit = await host.NewUnitAsync(host.Branch1);
        var owner = await host.EmployeeAsync(unit, "صاحب السجل");
        var other = await host.EmployeeAsync(unit, "موظف آخر");
        var day = new DateOnly(2025, 8, 4);
        using var branch = host.Branch();
        using (var created = await PostFormAsync(branch, "/DailyAttendanceRecords/Create", "/DailyAttendanceRecords/Create",
                   ("EmployeeId", owner.ToString("D")), ("AttendanceDate", "2025-08-04"), ("Status", "Present"), ("Notes", "سجل للتعديل")))
            Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var record = await RecordAsync(owner, day);
        Assert.NotNull(record);

        // The form posts another employee and another date beside the status: both have nothing to bind to.
        using (var edited = await PostFormAsync(branch, $"/DailyAttendanceRecords/Edit/{record!.Id:D}", $"/DailyAttendanceRecords/Edit/{record.Id:D}",
                   ("Id", record.Id.ToString("D")), ("EmployeeId", other.ToString("D")), ("AttendanceDate", "2025-08-20"), ("Status", "Absent"), ("Notes", "غياب بلا عذر")))
            Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);

        var after = await RecordAsync(owner, day);
        Assert.Equal((record.Id, owner, day, DailyAttendanceStatus.Absent, "غياب بلا عذر"),
            (after!.Id, after.EmployeeId, after.AttendanceDate, after.Status, after.Notes));
        Assert.Null(await RecordAsync(other, new DateOnly(2025, 8, 20)));
        Assert.Null(await RecordAsync(other, day));
        // The change is in the audit log, as an ordinary save writes it.
        Assert.True(await host.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.EntityId == record.Id)));

        // The edit page shows the employee and the date as text, and offers no «في إجازة».
        var page = WebUtility.HtmlDecode(await (await branch.GetAsync($"/DailyAttendanceRecords/Edit/{record.Id:D}")).Content.ReadAsStringAsync());
        Assert.Contains("صاحب السجل الحضور", page);
        Assert.DoesNotContain("name=\"EmployeeId\"", page);
        Assert.DoesNotContain("name=\"AttendanceDate\"", page);
        Assert.DoesNotContain("في إجازة", page);

        // «في إجازة» by hand: refused on a create and on an edit, and nothing changes.
        using (var onLeave = await PostFormAsync(branch, "/DailyAttendanceRecords/Create", "/DailyAttendanceRecords/Create",
                   ("EmployeeId", owner.ToString("D")), ("AttendanceDate", "2025-08-05"), ("Status", "OnLeave")))
        {
            Assert.Equal(HttpStatusCode.OK, onLeave.StatusCode);
            Assert.Contains("تأتي من إجازة معتمدة فقط", WebUtility.HtmlDecode(await onLeave.Content.ReadAsStringAsync()));
        }

        Assert.Null(await RecordAsync(owner, new DateOnly(2025, 8, 5)));
        using (var onLeave = await PostFormAsync(branch, $"/DailyAttendanceRecords/Edit/{record.Id:D}", $"/DailyAttendanceRecords/Edit/{record.Id:D}",
                   ("Id", record.Id.ToString("D")), ("Status", "OnLeave")))
            Assert.Equal(HttpStatusCode.OK, onLeave.StatusCode);
        Assert.Equal(DailyAttendanceStatus.Absent, (await RecordAsync(owner, day))!.Status);

        // A duplicate day is a message on the form, never an error page.
        using (var duplicate = await PostFormAsync(branch, "/DailyAttendanceRecords/Create", "/DailyAttendanceRecords/Create",
                   ("EmployeeId", owner.ToString("D")), ("AttendanceDate", "2025-08-04"), ("Status", "Present")))
        {
            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
            Assert.Contains("يوجد سجل حضور لهذا الموظف في هذا اليوم", WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync()));
        }
    }

    [Fact]
    public async Task A_leave_made_record_is_not_edited_or_deleted_from_the_screens()
    {
        var unit = await host.NewUnitAsync(host.Branch1);
        var employee = await host.EmployeeAsync(unit);
        var record = await host.WithDbAsync(async db =>
        {
            // What an approved leave leaves (the consumer writes it through the writer; here it is the test's starting point).
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "DailyAttendanceRecords" ("Id","TenantId","CreatedAt","UpdatedAt","EmployeeId","AttendanceDate","Status","Source","SourceReference")
                VALUES ({id}, {Lva057HrCoreApiHost.Tenant}, {DateTime.UtcNow}, {DateTime.UtcNow}, {employee}, {new DateOnly(2025, 9, 2)}, 2, 2, {"LeaveRequestId=" + Guid.NewGuid().ToString("D")})
                """);
            return id;
        });
        using var central = host.Central();

        using (var edit = await central.GetAsync($"/DailyAttendanceRecords/Edit/{record:D}"))
            Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);
        using (var deleted = await PostFormAsync(central, "/DailyAttendanceRecords/Create", $"/DailyAttendanceRecords/Delete/{record:D}"))
            Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);

        var after = await RecordAsync(employee, new DateOnly(2025, 9, 2));
        Assert.Equal((DailyAttendanceStatus.OnLeave, AttendanceRecordSource.Leave), (after!.Status, after.Source));
    }

    [Fact]
    public async Task The_bulk_entry_fills_working_days_only_and_the_pages_open()
    {
        var unit = await host.NewUnitAsync(host.Branch1);
        var employee = await host.EmployeeAsync(unit);
        await FillAsync(unit, "2025-02");

        // February 2025: 20 working days, less the configured holiday of Monday 17 February.
        var days = await host.WithDbAsync(db => db.DailyAttendanceRecords.AsNoTracking().Where(r => r.EmployeeId == employee).Select(r => r.AttendanceDate).ToListAsync());
        Assert.Equal(19, days.Count);
        Assert.DoesNotContain(days, d => d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday);
        Assert.DoesNotContain(new DateOnly(2025, 2, 17), days);

        // The current month: nothing after today is written.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HRManagement.ServiceDefaults.Localization.HrLocalTime.Zone));
        await FillAsync(unit, today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, await host.WithDbAsync(db => db.DailyAttendanceRecords.CountAsync(r => r.EmployeeId == employee && r.AttendanceDate > today)));

        using var branch = host.Branch();
        foreach (var path in new[] { "/AttendancePeriods", "/AttendancePeriods/Close", "/AttendanceCorrections", "/DailyAttendanceRecords", "/DailyAttendanceRecords/BulkEntry", "/DailyAttendanceRecords/Create" })
        {
            using var page = await branch.GetAsync(path);
            Assert.True(page.StatusCode == HttpStatusCode.OK, $"{path}: {(int)page.StatusCode}");
        }

        // The old close's address leads to the new page.
        using var old = await branch.GetAsync("/DailyAttendanceRecords/ClosePeriod");
        Assert.Equal(HttpStatusCode.Redirect, old.StatusCode);
        Assert.Contains("/AttendancePeriods/Close", old.Headers.Location!.ToString());
        // The old POST is gone.
        using var oldPost = await branch.PostAsync("/DailyAttendanceRecords/ClosePeriod", new FormUrlEncodedContent([new("periodStart", "2025-02-01")]));
        Assert.Contains(oldPost.StatusCode, new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound, HttpStatusCode.BadRequest });
        Assert.Equal(0, await host.WithDbAsync(db => db.AttendancePeriods.CountAsync(p => p.PeriodStart == new DateOnly(2025, 2, 1))));
    }
}
