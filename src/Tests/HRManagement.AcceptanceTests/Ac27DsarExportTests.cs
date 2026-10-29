using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Compliance.Domain.Enums;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-27: real DSAR package on fulfill with masked fields + audit hash.</summary>
public class Ac27DsarExportTests
{
    [SkippableFact]
    public async Task Fulfill_produces_masked_package_without_raw_national_id()
    {
        AcceptanceEnv.RequireEnabled();
        var complianceOfficer = AcceptanceHarness.RequirePersona("a-compliance");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(complianceOfficer);
        var recorder = new EvidenceRecorder("AC-27", correlationId);
        recorder.WriteMetadata("AC-27 DSAR export (PRV-002 / GOV-001)", complianceOfficer.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(complianceOfficer, correlationId);

        var createPayload = new
        {
            requestType = DsarRequestType.Access,
            subjectEmployeeId = AcceptanceSeedIds.EmpBr1Id,
            notes = "AC-27 acceptance"
        };
        var createResp = await http.PostAsJsonAsync("api/compliance/api/dsar", createPayload);
        var createBody = await createResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-create-dsar", "POST", "/api/compliance/api/dsar",
            (int)createResp.StatusCode, JsonSerializer.Deserialize<object>(createBody));
        AcceptanceGate.RequireData(recorder, createResp.StatusCode != HttpStatusCode.NotFound,
            "خدمة الامتثال غير منشورة في بيئة القبول",
            "Compliance service not deployed in acceptance stack.");

        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        using var createDoc = JsonDocument.Parse(createBody);
        var dsarId = createDoc.RootElement.GetProperty("id").GetGuid();

        var progressPayload = new { status = DsarRequestStatus.InProgress, notes = (string?)null, discoveryPackage = (object?)null };
        var progressResp = await http.PostAsJsonAsync($"api/compliance/api/dsar/{dsarId:D}/status", progressPayload);
        var progressBody = await progressResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-in-progress", "POST", $"/api/compliance/api/dsar/{dsarId:D}/status",
            (int)progressResp.StatusCode, JsonSerializer.Deserialize<object>(progressBody));
        Assert.Equal(HttpStatusCode.OK, progressResp.StatusCode);

        var fulfillResp = await http.PostAsync($"api/compliance/api/dsar/{dsarId:D}/fulfill", null);
        var fulfillBody = await fulfillResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-fulfill", "POST", $"/api/compliance/api/dsar/{dsarId:D}/fulfill",
            (int)fulfillResp.StatusCode, JsonSerializer.Deserialize<object>(fulfillBody));
        Assert.Equal(HttpStatusCode.OK, fulfillResp.StatusCode);

        var packageResp = await http.GetAsync($"api/compliance/api/dsar/{dsarId:D}/package");
        var packageJson = await packageResp.Content.ReadAsStringAsync();
        recorder.WriteBefore(new { dsarId, packageLength = packageJson.Length });
        File.WriteAllText(Path.Combine(recorder.Folder, "package.json"), packageJson);

        using var packageDoc = JsonDocument.Parse(packageJson);
        var packageHash = packageDoc.RootElement.GetProperty("packageContentHash").GetString();
        var sources = packageDoc.RootElement.GetProperty("sources").EnumerateArray().ToArray();

        recorder.WriteAudit(new
        {
            dsarId,
            exporterPersona = complianceOfficer.Id,
            packageContentHash = packageHash
        });

        Assert.DoesNotMatch(new Regex(@"\b\d{11}\b"), packageJson);
        Assert.False(string.IsNullOrWhiteSpace(packageHash));
        Assert.NotEmpty(sources);
        Assert.All(sources, source =>
            Assert.True(source.GetProperty("collected").GetBoolean(), source.GetProperty("error").GetString()));

        recorder.WriteAfter(new { dsarId, packageContentHash = packageHash, masked = true });
        recorder.WriteResult("Pass");
    }
}
