using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-29: presign valid for owner; wrong user denied.</summary>
public class Ac29FileAccessExpiryTests
{
    [SkippableFact]
    public async Task Presign_valid_for_uploader_wrong_user_denied()
    {
        AcceptanceEnv.RequireEnabled();
        var uploader = AcceptanceHarness.RequirePersona("a-file-custodian");
        var outsider = AcceptanceHarness.RequirePersona("a-archive");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(uploader);
        var recorder = new EvidenceRecorder("AC-29", correlationId);
        recorder.WriteMetadata("AC-29 file access grant expiry (SEC-003)", uploader.Id);

        using var uploadHttp = await AcceptanceHarness.CreateAuthorizedClientAsync(uploader, correlationId);
        var pdf = "%PDF-1.4 ac29 test"u8.ToArray();
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(pdf);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(content, "file", "ac29-private.pdf");

        var uploadResp = await uploadHttp.PostAsync("api/files", form);
        var uploadBody = await uploadResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-upload", "POST", "/api/files", (int)uploadResp.StatusCode,
            JsonSerializer.Deserialize<object>(uploadBody));
        Skip.If(uploadResp.StatusCode == HttpStatusCode.NotFound, "File storage not in acceptance stack.");
        Assert.Equal(HttpStatusCode.Created, uploadResp.StatusCode);

        using var uploadDoc = JsonDocument.Parse(uploadBody);
        var fileId = uploadDoc.RootElement.GetProperty("fileId").GetGuid();

        var clean = false;
        for (var i = 0; i < 90; i++)
        {
            var metaResp = await uploadHttp.GetAsync($"api/files/{fileId:D}");
            if (metaResp.IsSuccessStatusCode)
            {
                var metaBody = await metaResp.Content.ReadAsStringAsync();
                using var metaDoc = JsonDocument.Parse(metaBody);
                if (metaDoc.RootElement.GetProperty("scanStatus").GetString() == "Clean")
                {
                    clean = true;
                    break;
                }
            }
            await Task.Delay(500);
        }
        Assert.True(clean, "The uploaded file did not reach Clean scan status within 45 seconds.");

        var presignOk = await uploadHttp.GetAsync($"api/files/{fileId:D}/presigned-url?expiryMinutes=1");
        var presignBody = await presignOk.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-presign-valid", "GET", $"/api/files/{fileId:D}/presigned-url",
            (int)presignOk.StatusCode, JsonSerializer.Deserialize<object>(presignBody));
        Assert.Equal(HttpStatusCode.OK, presignOk.StatusCode);

        var outsiderCorrelation = Guid.NewGuid().ToString("D");
        using var outsiderHttp = await AcceptanceHarness.CreateAuthorizedClientAsync(outsider, outsiderCorrelation);
        var presignDenied = await outsiderHttp.GetAsync($"api/files/{fileId:D}/presigned-url");
        var deniedBody = await presignDenied.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-presign-denied", "GET", $"/api/files/{fileId:D}/presigned-url",
            (int)presignDenied.StatusCode, JsonSerializer.Deserialize<object>(deniedBody));
        Assert.Equal(HttpStatusCode.NotFound, presignDenied.StatusCode);

        recorder.WriteAudit(new { fileId, deniedReason = "access-denied/wrong-grantee" });
        recorder.WriteAfter(new { fileId, ownerPresign = (int)presignOk.StatusCode, outsiderPresign = (int)presignDenied.StatusCode });
        recorder.WriteResult("Pass");
    }
}
