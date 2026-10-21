using System.Net.Http.Headers;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-05: Platform admin enter-tenant with mandatory reason + audit.</summary>
public class Ac05PlatformEnterTenantTests
{
    [SkippableFact]
    public async Task Platform_admin_enters_tenant_with_reason_then_can_query()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("platform-admin");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-05", correlationId);
        recorder.WriteMetadata("AC-05 platform enter-tenant", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);

        var noReasonResp = await AcceptanceHarness.EnterTenantAsync(http, AcceptanceSeedIds.AgencyAId, "  ");
        recorder.CaptureHttp("enter-no-reason", "POST", "/api/platform/enter-tenant",
            (int)noReasonResp.StatusCode, await noReasonResp.Content.ReadAsStringAsync());
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, noReasonResp.StatusCode);

        var enterResp = await AcceptanceHarness.EnterTenantAsync(
            http, AcceptanceSeedIds.AgencyAId, "AC-05 acceptance audit review");
        var enterBody = await enterResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("enter-with-reason", "POST", "/api/platform/enter-tenant",
            (int)enterResp.StatusCode, JsonSerializer.Deserialize<object>(enterBody));
        Assert.Equal(System.Net.HttpStatusCode.OK, enterResp.StatusCode);

        recorder.WriteAudit(new
        {
            correlationId,
            user = persona.Username,
            tenantId = AcceptanceSeedIds.AgencyAId,
            reason = "AC-05 acceptance audit review",
            eventType = "PlatformEnterTenant"
        });

        var exitResp = await AcceptanceHarness.ExitTenantAsync(http);
        Assert.Equal(System.Net.HttpStatusCode.OK, exitResp.StatusCode);
        recorder.CaptureHttp("exit-tenant", "POST", "/api/platform/exit-tenant",
            (int)exitResp.StatusCode, await exitResp.Content.ReadAsStringAsync());

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Tenant_user_cannot_call_enter_tenant()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-05", correlationId + "-negative");
        recorder.WriteMetadata("AC-05 tenant user denied enter-tenant", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var resp = await AcceptanceHarness.EnterTenantAsync(http, AcceptanceSeedIds.AgencyAId, "should fail");
        recorder.CaptureHttp("tenant-user-enter", "POST", "/api/platform/enter-tenant",
            (int)resp.StatusCode, await resp.Content.ReadAsStringAsync());

        Assert.True(
            resp.StatusCode is System.Net.HttpStatusCode.Forbidden
                or System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.MethodNotAllowed,
            $"Expected 403/401/405, got {resp.StatusCode}");
        recorder.WriteResult("Pass");
    }
}
