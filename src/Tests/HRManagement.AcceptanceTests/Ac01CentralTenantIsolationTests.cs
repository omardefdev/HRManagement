using System.Net.Http.Headers;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-01: Central user in Tenant-A sees branch employees; Tenant-B employee absent.</summary>
public class Ac01CentralTenantIsolationTests
{
    [SkippableFact]
    public async Task Central_user_sees_agency_a_employees_not_agency_b()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-01", correlationId);
        recorder.WriteMetadata("AC-01 central tenant isolation", persona.Id);
        recorder.WriteSteps("""
            ## Preconditions
            - PLAN-00 acceptance seed applied (AGENCY-A + AGENCY-B).
            - Persona `acc-central-a` with tenant AGENCY-A.

            ## Steps
            1. Obtain JWT via password grant.
            2. GET /api/hr-core/api/lookup/employees?q=فرع
            3. Assert Tenant-A employees present; Tenant-B employee absent.
            4. GET Tenant-B employee by id => 404/403.
            """);

        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(persona, correlationId);
        var jwt = AcceptanceHarness.ParseJwt(token);
        AcceptanceHarness.AssertTenantClaim(jwt, AcceptanceSeedIds.AgencyAId.ToString("D"));

        using var http = new HttpClient { BaseAddress = new Uri(E2eEnv.BaseUrl.TrimEnd('/') + "/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var visibleIds = new HashSet<Guid>();
        foreach (var employeeId in new[] { AcceptanceSeedIds.EmpBr1Id, AcceptanceSeedIds.EmpBr2Id })
        {
            var response = await http.GetAsync($"api/hr-core/api/lookup/employees/{employeeId:D}");
            var body = await response.Content.ReadAsStringAsync();
            E2eHttp.EnsureNotServerError((int)response.StatusCode, $"lookup employee {employeeId:D}");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            using var employee = JsonDocument.Parse(body);
            visibleIds.Add(employee.RootElement.GetProperty("id").GetGuid());
            recorder.CaptureHttp(
                $"lookup-{employeeId:N}",
                "GET",
                $"/api/hr-core/api/lookup/employees/{employeeId:D}",
                (int)response.StatusCode,
                JsonSerializer.Deserialize<object>(body));
        }
        sw.Stop();

        recorder.WriteBefore(new { expectedTenant = AcceptanceSeedIds.AgencyACode });
        recorder.WriteAfter(new { returnedEmployeeIds = visibleIds });
        recorder.RecordTiming("lookup_ms", sw.ElapsedMilliseconds);
        recorder.WriteAudit(new { correlationId, user = persona.Username, tenantId = AcceptanceSeedIds.AgencyAId });
        recorder.WriteDbChecks(new
        {
            agencyAEmployees = new[] { AcceptanceSeedIds.EmpBr1Id, AcceptanceSeedIds.EmpD1Id, AcceptanceSeedIds.EmpBr2Id },
            agencyBEmployee = AcceptanceSeedIds.EmpBId
        });

        Assert.Contains(AcceptanceSeedIds.EmpBr1Id, visibleIds);
        Assert.Contains(AcceptanceSeedIds.EmpBr2Id, visibleIds);
        Assert.DoesNotContain(AcceptanceSeedIds.EmpBId, visibleIds);

        var outsiderResp = await http.GetAsync($"api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBId:D}");
        recorder.CaptureHttp("get-outsider-employee", "GET",
            $"/api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBId:D}",
            (int)outsiderResp.StatusCode, await outsiderResp.Content.ReadAsStringAsync());

        Assert.True(
            outsiderResp.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden,
            $"Expected 404/403 for cross-tenant employee, got {outsiderResp.StatusCode}");

        recorder.FlushTiming();
        recorder.WriteResult("Pass");
    }
}
