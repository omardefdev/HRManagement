using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Tenancy;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-08: quarantine blocks ops until resolve API assigns tenant.</summary>
public class Ac08QuarantineReconciliationTests
{
    [SkippableFact]
    public async Task Quarantined_employee_blocked_then_resolved()
    {
        AcceptanceEnv.RequireEnabled();
        var admin = AcceptanceHarness.RequirePersona("platform-admin");
        var central = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(admin);
        var recorder = new EvidenceRecorder("AC-08", correlationId);
        recorder.WriteMetadata("AC-08 quarantine reconciliation", admin.Id);

        Environment.SetEnvironmentVariable("BASE_URL", E2eEnv.BaseUrl.TrimEnd('/'));
        var (scriptExit, scriptOutput) = await AcceptanceMessagingHelper.RunScriptAsync("acceptance-quarantine-flow.ps1");
        recorder.WriteDbChecks(new { scriptExitCode = scriptExit, scriptOutput });
        Assert.Equal(0, scriptExit);
        using var scriptReport = AcceptanceMessagingHelper.TryParseJson(scriptOutput);
        Assert.NotNull(scriptReport);

        using var adminHttp = await AcceptanceHarness.CreateAuthorizedClientAsync(admin, correlationId);

        var listResp = await adminHttp.GetAsync("api/hr-core/api/tenant-quarantine?resolved=false");
        var listBody = await listResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("reconcile-before", "GET", "/api/tenant-quarantine?resolved=false",
            (int)listResp.StatusCode, JsonSerializer.Deserialize<object>(listBody));

        var centralCorrelation = Guid.NewGuid().ToString("D");
        using var centralHttp = await AcceptanceHarness.CreateAuthorizedClientAsync(central, centralCorrelation);

        var employeeId = scriptReport.RootElement.GetProperty("EmployeeId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(employeeId));

        var blocked = await centralHttp.PatchAsJsonAsync(
            $"api/hr-core/api/employees/{employeeId}",
            new { notes = "blocked attempt" });
        var blockedBody = await blocked.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-blocked-op", "PATCH", $"/api/employees/{employeeId}",
            (int)blocked.StatusCode, JsonSerializer.Deserialize<object>(blockedBody));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains(QuarantinedRecordException.ErrorCode, blockedBody, StringComparison.OrdinalIgnoreCase);

        var entryId = scriptReport.RootElement.GetProperty("QuarantineEntryId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(entryId));

        var resolvePayload = new
        {
            resolution = "AssignTenant",
            assignedTenantId = AcceptanceSeedIds.AgencyAId,
            reason = "AC-08 acceptance reconciliation"
        };
        var resolveResp = await adminHttp.PostAsJsonAsync(
            $"api/hr-core/api/tenant-quarantine/{entryId}/resolve", resolvePayload);
        var resolveBody = await resolveResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-resolve", "POST", $"/api/tenant-quarantine/{entryId}/resolve",
            (int)resolveResp.StatusCode, JsonSerializer.Deserialize<object>(resolveBody));
        Assert.Equal(HttpStatusCode.OK, resolveResp.StatusCode);

        var afterList = await adminHttp.GetAsync("api/hr-core/api/tenant-quarantine?resolved=false");
        recorder.CaptureHttp("reconcile-after", "GET", "/api/tenant-quarantine?resolved=false",
            (int)afterList.StatusCode, JsonSerializer.Deserialize<object>(await afterList.Content.ReadAsStringAsync()));

        var success = await centralHttp.PatchAsJsonAsync(
            $"api/hr-core/api/employees/{employeeId}",
            new { notes = "after resolve" });
        recorder.CaptureHttp("http-after-resolve", "PATCH", $"/api/employees/{employeeId}",
            (int)success.StatusCode, null);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);

        recorder.WriteResult("Pass");
    }
}
