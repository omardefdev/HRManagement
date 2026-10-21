using System.Net.Http.Headers;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-04: Cross-tenant employee id guess blocked at service layer.</summary>
public class Ac04CrossTenantGuessTests
{
    [SkippableFact]
    public async Task Tenant_a_user_cannot_guess_tenant_b_employee()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-04", correlationId);
        recorder.WriteMetadata("AC-04 cross-tenant id guess", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);

        var resp = await http.GetAsync($"api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBId:D}");
        var body = await resp.Content.ReadAsStringAsync();

        recorder.CaptureHttp("guess-outsider-employee", "GET",
            $"/api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBId:D}",
            (int)resp.StatusCode, body);
        recorder.WriteDbChecks(new
        {
            layers = new[]
            {
                "gateway: tenant_id enforced (GatewayTenantBarrierTests)",
                "service: org/tenant scope on lookup API",
                "query: EF tenant filter",
                "rls: CrossTenantRlsIntegrationTests with RUN_TESTCONTAINERS=true"
            },
            guessedEmployeeId = AcceptanceSeedIds.EmpBId,
            callerTenant = AcceptanceSeedIds.AgencyAId
        });

        Assert.True(
            resp.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden,
            $"Expected 404/403, got {resp.StatusCode}");

        recorder.WriteResult("Pass");
    }
}
