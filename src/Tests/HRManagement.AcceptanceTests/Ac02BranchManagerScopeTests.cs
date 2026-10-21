using System.Net.Http.Headers;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-02: Branch manager BR1 sees BR1 + department only.</summary>
public class Ac02BranchManagerScopeTests
{
    [SkippableFact]
    public async Task Br1_manager_sees_branch1_employees_not_sibling_branch2()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-02", correlationId);
        recorder.WriteMetadata("AC-02 branch manager org scope", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var visibleIds = new HashSet<Guid>();
        foreach (var employeeId in new[] { AcceptanceSeedIds.EmpBr1Id, AcceptanceSeedIds.EmpD1Id })
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

        var siblingResponse = await http.GetAsync(
            $"api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr2Id:D}");
        recorder.CaptureHttp(
            "lookup-sibling",
            "GET",
            $"/api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr2Id:D}",
            (int)siblingResponse.StatusCode,
            await siblingResponse.Content.ReadAsStringAsync());
        Assert.True(siblingResponse.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound);

        recorder.WriteAfter(new { returnedEmployeeIds = visibleIds, siblingStatus = siblingResponse.StatusCode });
        recorder.WriteDbChecks(new
        {
            allowedOrgUnits = new[] { AcceptanceSeedIds.Br1OrgUnitId, AcceptanceSeedIds.Br1D1OrgUnitId },
            expectedVisible = new[] { AcceptanceSeedIds.EmpBr1Id, AcceptanceSeedIds.EmpD1Id },
            siblingHidden = AcceptanceSeedIds.EmpBr2Id
        });

        Assert.Contains(AcceptanceSeedIds.EmpBr1Id, visibleIds);
        Assert.Contains(AcceptanceSeedIds.EmpD1Id, visibleIds);
        Assert.DoesNotContain(AcceptanceSeedIds.EmpBr2Id, visibleIds);

        recorder.WriteResult("Pass");
    }
}
