using System.Net.Http.Headers;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-03: Sibling branch access denied with audit context.</summary>
public class Ac03SiblingAccessDeniedTests
{
    [SkippableFact]
    public async Task Br1_manager_denied_sibling_employee_via_api()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-03", correlationId);
        recorder.WriteMetadata("AC-03 sibling org-scope denial", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);

        var siblingResp = await http.GetAsync(
            $"api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr2Id:D}");
        var siblingBody = await siblingResp.Content.ReadAsStringAsync();

        recorder.CaptureHttp("get-sibling-employee", "GET",
            $"/api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr2Id:D}",
            (int)siblingResp.StatusCode, siblingBody);
        recorder.WriteAudit(new
        {
            correlationId,
            user = persona.Username,
            tenantId = AcceptanceSeedIds.AgencyAId,
            scopeOrgUnitId = AcceptanceSeedIds.Br1OrgUnitId,
            reason = "org-scope-denied",
            resourceType = "Employee",
            resourceId = AcceptanceSeedIds.EmpBr2Id
        });

        Assert.True(
            siblingResp.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound,
            $"Expected 403/404, got {siblingResp.StatusCode}");
        Assert.DoesNotContain(AcceptanceSeedIds.EmpBr2Id.ToString("D"), siblingBody);

        recorder.WriteResult("Pass");
    }
}
