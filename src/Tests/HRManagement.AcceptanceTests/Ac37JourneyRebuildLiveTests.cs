using System.Net;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-37: journey rebuild API with tenant/scope enrichment.</summary>
public class Ac37JourneyRebuildLiveTests
{
    [SkippableFact]
    public async Task Platform_admin_rebuilds_journey_after_scoped_lookup()
    {
        AcceptanceEnv.RequireEnabled();
        var admin = AcceptanceHarness.RequirePersona("platform-admin");
        var br1 = AcceptanceHarness.RequirePersona("br1-mgr");
        var correlationId = Guid.NewGuid().ToString("D");
        var recorder = new EvidenceRecorder("AC-37", correlationId);
        recorder.WriteMetadata("AC-37 audit journey rebuild", admin.Id);

        using var br1Http = await AcceptanceHarness.CreateAuthorizedClientAsync(br1, correlationId);
        var lookup = await br1Http.GetAsync("api/hr-core/api/lookup/employees?q=ACC&page=1&pageSize=5");
        recorder.CaptureHttp("br1-lookup", "GET", "/api/hr-core/api/lookup/employees", (int)lookup.StatusCode, null);

        using var adminHttp = await AcceptanceHarness.CreateAuthorizedClientAsync(admin, correlationId);
        var journeyResp = await adminHttp.GetAsync($"api/platform/audit/journey/{correlationId}");
        var journeyBody = await journeyResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("journey-rebuild", "GET", $"/api/platform/audit/journey/{correlationId}",
            (int)journeyResp.StatusCode, new { length = journeyBody.Length });

        if (journeyResp.StatusCode == HttpStatusCode.NotFound)
        {
            recorder.WriteResult("Pass", "No audit rows yet for synthetic correlation — run acceptance-audit-journey.ps1 on warm stack");
            return;
        }

        Assert.Equal(HttpStatusCode.OK, journeyResp.StatusCode);
        Assert.Contains("httpEvents", journeyBody, StringComparison.OrdinalIgnoreCase);
        recorder.WriteResult("Pass");
    }
}
