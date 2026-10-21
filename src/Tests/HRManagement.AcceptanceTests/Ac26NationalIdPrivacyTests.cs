using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-26: hash search + role-based full/masked national ID + PII scan script.</summary>
public class Ac26NationalIdPrivacyTests
{
    [SkippableFact]
    public async Task Hash_search_and_role_based_display()
    {
        AcceptanceEnv.RequireEnabled();
        var central = AcceptanceHarness.RequirePersona("central-a");
        var branchMgr = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(central);
        var recorder = new EvidenceRecorder("AC-26", correlationId);
        recorder.WriteMetadata("AC-26 national ID privacy (PRV-001)", central.Id);

        using var centralHttp = await AcceptanceHarness.CreateAuthorizedClientAsync(central, correlationId);
        var hash = NationalIdSearch.ComputeHash(AcceptanceSeedIds.NationalIdBr1);

        var hashResp = await centralHttp.GetAsync($"api/hr-core/api/employees/by-national-id-hash?hash={hash}");
        var hashBody = await hashResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-search-by-hash", "GET",
            $"/api/hr-core/api/employees/by-national-id-hash?hash={hash}",
            (int)hashResp.StatusCode, JsonSerializer.Deserialize<object>(hashBody));
        Assert.Equal(HttpStatusCode.OK, hashResp.StatusCode);

        using (var hashDoc = JsonDocument.Parse(hashBody))
        {
            var display = hashDoc.RootElement.GetProperty("displayNationalId").GetString();
            Assert.Equal(AcceptanceSeedIds.NationalIdBr1, display);
        }

        var searchResp = await centralHttp.GetAsync(
            $"api/hr-core/api/employees/by-national-id-hash?hash={hash}");
        var searchBody = await searchResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-details-central", "GET",
            "/api/hr-core/api/employees/by-national-id-hash",
            (int)searchResp.StatusCode, JsonSerializer.Deserialize<object>(searchBody));
        Assert.Equal(HttpStatusCode.OK, searchResp.StatusCode);

        var branchCorrelation = Guid.NewGuid().ToString("D");
        using var branchHttp = await AcceptanceHarness.CreateAuthorizedClientAsync(branchMgr, branchCorrelation);
        var branchResp = await branchHttp.GetAsync(
            $"api/hr-core/api/employees/by-national-id-hash?hash={hash}");
        var branchBody = await branchResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-details-branch", "GET",
            "/api/hr-core/api/employees/by-national-id-hash",
            (int)branchResp.StatusCode, JsonSerializer.Deserialize<object>(branchBody));
        Assert.Equal(HttpStatusCode.OK, branchResp.StatusCode);

        using var branchDoc = JsonDocument.Parse(branchBody);
        var masked = branchDoc.RootElement.GetProperty("displayNationalId").GetString();
        Assert.NotEqual(AcceptanceSeedIds.NationalIdBr1, masked);
        Assert.Equal(PiiMask.NationalId(AcceptanceSeedIds.NationalIdBr1), masked);

        Environment.SetEnvironmentVariable("BASE_URL", E2eEnv.BaseUrl.TrimEnd('/'));
        var (exitCode, scriptOutput) = await AcceptanceMessagingHelper.RunScriptAsync("ac026-scan-pii.ps1");
        recorder.WriteDbChecks(new { scanExitCode = exitCode, scriptOutput });
        recorder.WriteAfter(new
        {
            centralDisplay = AcceptanceSeedIds.NationalIdBr1,
            branchDisplay = masked,
            scanExitCode = exitCode
        });

        Assert.Equal(0, exitCode);
        recorder.WriteResult("Pass");
    }
}
