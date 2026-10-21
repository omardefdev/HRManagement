using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-EST-14: COD preview for JobCatalogEntry codes.</summary>
public class AcEst14JobCatalogCodPreviewTests
{
    [SkippableFact]
    public async Task Preview_job_catalog_code_returns_non_empty_value()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-14", correlationId);
        recorder.WriteMetadata("AC-EST-14 JobCatalog COD preview", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var payload = new
        {
            entityKind = "JobCatalogEntry",
            preview = true,
            facts = new Dictionary<string, string>
            {
                ["GradeCode"] = "GR-05",
                ["JobClassificationCode"] = "JC-ADM"
            }
        };

        var response = await http.PostAsJsonAsync("api/hr-core/api/codes/preview", payload);
        var body = await response.Content.ReadAsStringAsync();
        recorder.CaptureHttp("preview-job-catalog-code", "POST", "/api/hr-core/api/codes/preview", (int)response.StatusCode, JsonSerializer.Deserialize<object>(body));

        Assert.True(response.IsSuccessStatusCode, body);
        using var doc = JsonDocument.Parse(body);
        var code = doc.RootElement.GetProperty("code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(code));

        recorder.WriteDbChecks(new { previewCode = code });
        recorder.WriteResult("Pass");
    }
}
