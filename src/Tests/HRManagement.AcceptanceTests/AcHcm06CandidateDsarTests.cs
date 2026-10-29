using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Compliance.Domain.Enums;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>HCM-006: DSAR Access for candidate subject via Compliance collector.</summary>
public class AcHcm06CandidateDsarTests
{
    [SkippableFact]
    public async Task Candidate_fulfill_produces_masked_package_without_raw_national_id()
    {
        AcceptanceEnv.RequireEnabled();
        var complianceOfficer = AcceptanceHarness.RequirePersona("a-compliance");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(complianceOfficer);
        var recorder = new EvidenceRecorder("HCM-06-DSAR", correlationId);
        recorder.WriteMetadata("HCM-006 candidate DSAR export", complianceOfficer.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(complianceOfficer, correlationId);

        var createPayload = new
        {
            requestType = DsarRequestType.Access,
            subjectNationalId = "AC-1-000001",
            notes = "HCM-006 candidate DSAR"
        };
        var createResp = await http.PostAsJsonAsync("api/compliance/api/dsar", createPayload);
        var createBody = await createResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-create-candidate-dsar", "POST", "/api/compliance/api/dsar",
            (int)createResp.StatusCode, JsonSerializer.Deserialize<object>(createBody));
        Skip.If(createResp.StatusCode == HttpStatusCode.NotFound,
            "Compliance service not deployed in acceptance stack.");

        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        using var createDoc = JsonDocument.Parse(createBody);
        var dsarId = createDoc.RootElement.GetProperty("id").GetGuid();

        var progressPayload = new { status = DsarRequestStatus.InProgress, notes = (string?)null, discoveryPackage = (object?)null };
        var progressResp = await http.PostAsJsonAsync($"api/compliance/api/dsar/{dsarId:D}/status", progressPayload);
        Assert.Equal(HttpStatusCode.OK, progressResp.StatusCode);

        var fulfillResp = await http.PostAsync($"api/compliance/api/dsar/{dsarId:D}/fulfill", null);
        var fulfillBody = await fulfillResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-fulfill", "POST", $"/api/compliance/api/dsar/{dsarId:D}/fulfill",
            (int)fulfillResp.StatusCode, JsonSerializer.Deserialize<object>(fulfillBody));
        Skip.If(fulfillResp.StatusCode == HttpStatusCode.BadRequest,
            "HR.Core candidate DSAR source unavailable in acceptance stack.");
        Assert.Equal(HttpStatusCode.OK, fulfillResp.StatusCode);

        var packageResp = await http.GetAsync($"api/compliance/api/dsar/{dsarId:D}/package");
        var packageJson = await packageResp.Content.ReadAsStringAsync();
        File.WriteAllText(Path.Combine(recorder.Folder, "candidate-package.json"), packageJson);

        Assert.DoesNotMatch(new Regex(@"\b\d{11}\b"), packageJson);
        // PLAN-30 K2: /careers/my code challenges are exported as counts only.
        Assert.DoesNotContain("codeHmac", packageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requestIp", packageJson, StringComparison.OrdinalIgnoreCase);
        using var packageDoc = JsonDocument.Parse(packageJson);
        Assert.False(string.IsNullOrWhiteSpace(
            packageDoc.RootElement.GetProperty("packageContentHash").GetString()));

        recorder.WriteResult("Pass");
    }
}
