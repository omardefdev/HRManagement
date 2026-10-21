using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-EST-15: bulk job catalog CSV import through governed change request.</summary>
public class AcEst15JobCatalogBulkImportTests
{
    [SkippableFact]
    public async Task Import_valid_csv_submit_applies_catalog_rows()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-15", correlationId);
        recorder.WriteMetadata("AC-EST-15 job catalog bulk import", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var csvPath = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "establishment", "job-catalog-sample.csv");
        Assert.True(File.Exists(csvPath), $"Missing sample CSV: {csvPath}");

        var beforeHasManager = await JobCatalogExistsAsync(http, "مدير إداري");

        await using var csvStream = File.OpenRead(csvPath);
        using var upload = new MultipartFormDataContent();
        var fileContent = new StreamContent(csvStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        upload.Add(fileContent, "file", "job-catalog-sample.csv");

        var importUrl = "api/hr-core/api/establishment/job-catalog/import?mode=Standard";
        var importResponse = await http.PostAsync(importUrl, upload);
        var importBody = await importResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("job-catalog-import", "POST", $"/{importUrl}", (int)importResponse.StatusCode, JsonSerializer.Deserialize<object>(importBody));
        Assert.True(importResponse.IsSuccessStatusCode, importBody);

        using var importDoc = JsonDocument.Parse(importBody);
        var changeRequestId = importDoc.RootElement.GetProperty("changeRequestId").GetGuid();
        var rowCount = importDoc.RootElement.GetProperty("rowCount").GetInt32();
        Assert.Equal(3, rowCount);

        var submitPayload = new
        {
            legalSourceId = Syr001LegislationIds.Law50_2004,
            decisionReference = "DEC-BULK-15"
        };
        var submitUrl = $"api/hr-core/api/establishment/changes/{changeRequestId:D}/submit";
        var submitResponse = await http.PostAsJsonAsync(submitUrl, submitPayload);
        var submitBody = await submitResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("submit-bulk-change", "POST", $"/{submitUrl}", (int)submitResponse.StatusCode, JsonSerializer.Deserialize<object>(submitBody));
        Assert.True(submitResponse.IsSuccessStatusCode, submitBody);

        Assert.True(await JobCatalogExistsAsync(http, "مدير إداري"));
        Assert.True(await JobCatalogExistsAsync(http, "محلل"));
        Assert.True(await JobCatalogExistsAsync(http, "كاتب"));

        recorder.WriteDbChecks(new { changeRequestId, rowCount, hadManagerBefore = beforeHasManager });
        recorder.WriteResult("Pass");
    }

    private static async Task<bool> JobCatalogExistsAsync(HttpClient http, string titleAr)
    {
        var url = $"api/hr-core/api/lookup/job-catalog?q={Uri.EscapeDataString(titleAr)}&page=1&pageSize=20";
        var response = await http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Any(i => i.GetProperty("label").GetString()?.Contains(titleAr, StringComparison.Ordinal) == true);
    }
}
