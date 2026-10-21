using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-EST-17: migration mode preserves LegacyJobCode on apply.</summary>
public class AcEst17JobCatalogMigrationLegacyTests
{
    [SkippableFact]
    public async Task Migration_mode_applies_legacy_job_code()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-17", correlationId);
        recorder.WriteMetadata("AC-EST-17 migration legacy JobCode", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var csvPath = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "establishment", "job-catalog-migration-legacy.csv");
        Assert.True(File.Exists(csvPath), $"Missing sample CSV: {csvPath}");

        await using var csvStream = File.OpenRead(csvPath);
        using var upload = new MultipartFormDataContent();
        var fileContent = new StreamContent(csvStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        upload.Add(fileContent, "file", "job-catalog-migration-legacy.csv");

        var importUrl = "api/hr-core/api/establishment/job-catalog/import?mode=Migration";
        var importResponse = await http.PostAsync(importUrl, upload);
        var importBody = await importResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("job-catalog-import-migration", "POST", $"/{importUrl}", (int)importResponse.StatusCode, JsonSerializer.Deserialize<object>(importBody));
        Assert.True(importResponse.IsSuccessStatusCode, importBody);

        using var importDoc = JsonDocument.Parse(importBody);
        var changeRequestId = importDoc.RootElement.GetProperty("changeRequestId").GetGuid();

        var submitPayload = new
        {
            legalSourceId = Syr001LegislationIds.Law50_2004,
            decisionReference = "DEC-MIG-17"
        };
        var submitUrl = $"api/hr-core/api/establishment/changes/{changeRequestId:D}/submit";
        var submitResponse = await http.PostAsJsonAsync(submitUrl, submitPayload);
        var submitBody = await submitResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("submit-migration-change", "POST", $"/{submitUrl}", (int)submitResponse.StatusCode, JsonSerializer.Deserialize<object>(submitBody));
        Assert.True(submitResponse.IsSuccessStatusCode, submitBody);

        var lookupUrl = "api/hr-core/api/lookup/job-catalog?q=LEG-001&page=1&pageSize=5";
        var lookupResponse = await http.GetAsync(lookupUrl);
        var lookupBody = await lookupResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("lookup-legacy-code", "GET", $"/{lookupUrl}", (int)lookupResponse.StatusCode, JsonSerializer.Deserialize<object>(lookupBody));
        Assert.True(lookupResponse.IsSuccessStatusCode, lookupBody);
        Assert.Contains("LEG-001", lookupBody, StringComparison.Ordinal);

        recorder.WriteDbChecks(new { changeRequestId, legacyJobCode = "LEG-001" });
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Standard_mode_rejects_legacy_job_code_column()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-17", correlationId);
        recorder.WriteMetadata("AC-EST-17 standard mode legacy rejection", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var csvPath = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "establishment", "job-catalog-migration-legacy.csv");
        await using var csvStream = File.OpenRead(csvPath);
        using var upload = new MultipartFormDataContent();
        var fileContent = new StreamContent(csvStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        upload.Add(fileContent, "file", "job-catalog-migration-legacy.csv");

        var importUrl = "api/hr-core/api/establishment/job-catalog/import?mode=Standard";
        var importResponse = await http.PostAsync(importUrl, upload);
        var importBody = await importResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("job-catalog-import-standard-legacy", "POST", $"/{importUrl}", (int)importResponse.StatusCode, JsonSerializer.Deserialize<object>(importBody));

        Assert.False(importResponse.IsSuccessStatusCode);
        Assert.Contains("LegacyJobCode", importBody, StringComparison.Ordinal);

        recorder.WriteResult("Pass");
    }
}
