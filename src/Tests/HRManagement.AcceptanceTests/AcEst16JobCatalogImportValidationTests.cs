using System.Net.Http.Headers;
using System.Text.Json;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-EST-16: invalid grade in CSV rejected with line reference.</summary>
public class AcEst16JobCatalogImportValidationTests
{
    [SkippableFact]
    public async Task Import_invalid_grade_is_rejected()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-16", correlationId);
        recorder.WriteMetadata("AC-EST-16 invalid grade CSV", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var csvPath = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "establishment", "job-catalog-invalid-grade.csv");
        Assert.True(File.Exists(csvPath), $"Missing sample CSV: {csvPath}");

        await using var csvStream = File.OpenRead(csvPath);
        using var upload = new MultipartFormDataContent();
        var fileContent = new StreamContent(csvStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        upload.Add(fileContent, "file", "job-catalog-invalid-grade.csv");

        var importUrl = "api/hr-core/api/establishment/job-catalog/import?mode=Standard";
        var importResponse = await http.PostAsync(importUrl, upload);
        var importBody = await importResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("job-catalog-import-invalid", "POST", $"/{importUrl}", (int)importResponse.StatusCode, JsonSerializer.Deserialize<object>(importBody));

        Assert.False(importResponse.IsSuccessStatusCode);
        Assert.Contains("السطر", importBody, StringComparison.Ordinal);

        recorder.WriteDbChecks(new { rejected = true, body = importBody });
        recorder.WriteResult("Pass");
    }
}
