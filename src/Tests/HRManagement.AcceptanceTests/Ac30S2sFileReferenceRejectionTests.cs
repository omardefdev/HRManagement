using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-30: bad S2S token on file references — no DB side effect.</summary>
public class Ac30S2sFileReferenceRejectionTests
{
    [SkippableFact]
    public async Task Rogue_service_token_rejected_without_new_reference()
    {
        AcceptanceEnv.RequireEnabled();
        var fileCustodian = AcceptanceHarness.RequirePersona("a-file-custodian");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(fileCustodian);
        var recorder = new EvidenceRecorder("AC-30", correlationId);
        recorder.WriteMetadata("AC-30 S2S file reference rejection (IAM-004)", fileCustodian.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(fileCustodian, correlationId);
        var pdf = "%PDF-1.4 ac30 seed"u8.ToArray();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(pdf) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf") } },
            "file", "ac30-seed.pdf");
        var uploadResp = await http.PostAsync("api/files", form);
        AcceptanceGate.RequireData(recorder, uploadResp.StatusCode != HttpStatusCode.NotFound,
            "خدمة تخزين الملفات غير منشورة", "File storage not deployed.");
        Assert.Equal(HttpStatusCode.Created, uploadResp.StatusCode);
        var uploadBody = await uploadResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(uploadBody);
        var fileId = doc.RootElement.GetProperty("fileId").GetGuid();

        recorder.WriteBefore(new { fileId });

        Environment.SetEnvironmentVariable("BASE_URL", E2eEnv.BaseUrl.TrimEnd('/'));
        var (exitCode, output) = await AcceptanceMessagingHelper.RunScriptAsync(
            "acceptance-s2s-negative.ps1",
            ("FileId", fileId.ToString("D")));
        recorder.WriteDbChecks(new { scriptExitCode = exitCode, scriptOutput = output });

        Assert.Equal(0, exitCode);
        recorder.WriteAfter(new { fileId, scriptExitCode = exitCode });
        recorder.WriteResult("Pass");
    }
}
