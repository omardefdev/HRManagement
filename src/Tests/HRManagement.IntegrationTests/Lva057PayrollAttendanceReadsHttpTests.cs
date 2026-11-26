using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.OrgScope;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-57 (§4): the two payroll reads PLAN-59 builds on, over HTTP on Payroll.Web with its real pipeline and PostgreSQL:
/// <c>GET api/payroll/runs/{id}/attendance-status</c> and <c>GET api/payroll/attendance/receipts?periodStart=</c>. A run of another
/// agency is 404, the receipts of another agency are not listed, a caller with no payroll role is refused, and the run's page shows
/// the attendance panel. Both are reads: nothing is written.
/// </summary>
[Collection("PostgreSql")]
public sealed class Lva057PayrollAttendanceReadsHttpTests(PostgreSqlFixture postgres)
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Guid OtherAgency = Guid.Parse("57000000-0000-4000-8000-0000000000b0");
    private static readonly string ProcessorRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor}";
    private static readonly string ApproverRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}";

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

    private Task<Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>> StartAsync() =>
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>.StartAsync(
            postgres, "PayrollDb", PayrollOrgScopeKeys.Registry, ProcessorRoles, ApproverRoles, "/EmployeePayProfiles/Create", SeedAsync,
            settings: new Dictionary<string, string?> { ["Seed:Profile"] = "None" });

    private static HttpClient As(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, string roles, Guid? agency = null)
    {
        var client = host.Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, (agency ?? Tenant).ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        return client;
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_two_reads_answer_what_payroll_knows_and_nothing_of_another_agency()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var closed = host.Records[typeof(EmployeePayProfile)].Own;
        var missing = host.Records[typeof(EmployeePayProfile)].Foreign;
        var start = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var run = new PayrollRun { TenantId = Tenant, PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1), RunDate = start.AddMonths(1) };
        var close = Lva057Summary.Close(closed, 2028, 1, absentDays: 2, tenant: Tenant);
        await host.WithDbAsync(async db =>
        {
            db.Add(run);
            await db.SaveChangesAsync();
            await new AttendanceSummaryHandoff((PayrollDbContext)db).ApplyAsync(close);
            await new AttendanceSummaryHandoff((PayrollDbContext)db).ApplyAsync(close.Next(absentDays: 1));
            return true;
        });
        var rowsBefore = await host.WithDbAsync(db => ((PayrollDbContext)db).PayrollAuditRecords.CountAsync());

        using var processor = As(host, ProcessorRoles);
        using var status = await processor.GetAsync($"/api/payroll/runs/{run.Id:D}/attendance-status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using (var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync()))
        {
            var body = json.RootElement;
            Assert.Equal(("Warn", true, 2), (body.GetProperty("gateMode").GetString(), body.GetProperty("applies").GetBoolean(), body.GetProperty("employeesInScope").GetInt32()));
            Assert.Equal([missing], body.GetProperty("missingAttendance").EnumerateArray().Select(e => e.GetGuid()).ToArray());
            Assert.Equal((0, 0), (body.GetProperty("carryForwardApplied").GetInt32(), body.GetProperty("carryForwardPending").GetInt32()));
        }

        using var receipts = await processor.GetAsync("/api/payroll/attendance/receipts?periodStart=2028-01-01");
        Assert.Equal(HttpStatusCode.OK, receipts.StatusCode);
        using (var json = JsonDocument.Parse(await receipts.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, json.RootElement.GetProperty("count").GetInt32());
            var receipt = json.RootElement.GetProperty("receipts")[0];
            Assert.Equal((close.SummaryId, closed, 2, 1, 1),
                (receipt.GetProperty("summaryId").GetGuid(), receipt.GetProperty("employeeId").GetGuid(), receipt.GetProperty("latestVersion").GetInt32(),
                 receipt.GetProperty("latestAbsentDays").GetInt32(), receipt.GetProperty("recordedAbsentDays").GetInt32()));
        }

        // Another month has none; a missing or unreadable period is a 400.
        using (var other = await processor.GetAsync("/api/payroll/attendance/receipts?periodStart=2028-02-01"))
        using (var json = JsonDocument.Parse(await other.Content.ReadAsStringAsync()))
            Assert.Equal(0, json.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await processor.GetAsync("/api/payroll/attendance/receipts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await processor.GetAsync($"/api/payroll/runs/{Guid.NewGuid():D}/attendance-status")).StatusCode);

        // Another agency: the run does not exist for it, and it lists no receipt of this one.
        using var foreigner = As(host, ProcessorRoles, OtherAgency);
        Assert.Equal(HttpStatusCode.NotFound, (await foreigner.GetAsync($"/api/payroll/runs/{run.Id:D}/attendance-status")).StatusCode);
        using (var theirs = await foreigner.GetAsync("/api/payroll/attendance/receipts?periodStart=2028-01-01"))
        using (var json = JsonDocument.Parse(await theirs.Content.ReadAsStringAsync()))
            Assert.Equal(0, json.RootElement.GetProperty("count").GetInt32());

        // A caller with no payroll role reaches neither.
        using var hrCore = As(host, HrManagementRoles.HrCore);
        Assert.Equal(HttpStatusCode.Forbidden, (await hrCore.GetAsync($"/api/payroll/runs/{run.Id:D}/attendance-status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hrCore.GetAsync("/api/payroll/attendance/receipts?periodStart=2028-01-01")).StatusCode);

        // The run's page shows the panel: the mode, and one employee whose attendance is not closed.
        using var page = await processor.GetAsync($"/PayrollRuns/Details/{run.Id:D}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Contains("data-testid=\"run-attendance\"", html);
        Assert.Matches("data-testid=\"run-attendance-missing-count\">\\s*1\\s*<", html);
        Assert.Contains("data-testid=\"run-attendance-missing-names\"", html);

        // Both are reads: no audit record was written by asking.
        Assert.Equal(rowsBefore, await host.WithDbAsync(db => ((PayrollDbContext)db).PayrollAuditRecords.CountAsync()));
    }
}
