using System.Net.Http.Headers;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-EST-12: finance CSV import + reconciliation variance (stub — no live ERP).</summary>
public class AcEst12FinanceReconciliationTests
{
    [SkippableFact]
    public async Task Import_csv_then_reconciliation_shows_variance()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-12", correlationId);
        recorder.WriteMetadata("AC-EST-12 finance import + reconciliation", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var fiscalYear = AcceptanceSeedCatalog.ReferenceDate.Year;
        var csvPath = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "establishment", "br1-budget-sample.csv");
        Assert.True(File.Exists(csvPath), $"Missing sample CSV: {csvPath}");

        await using var csvStream = File.OpenRead(csvPath);
        using var upload = new MultipartFormDataContent();
        var fileContent = new StreamContent(csvStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        upload.Add(fileContent, "file", "br1-budget-sample.csv");

        var importUrl = $"api/hr-core/api/establishment/finance/import?fiscalYear={fiscalYear}";
        var importResponse = await http.PostAsync(importUrl, upload);
        var importBody = await importResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("finance-import", "POST", $"/{importUrl}", (int)importResponse.StatusCode, JsonSerializer.Deserialize<object>(importBody));
        Assert.True(importResponse.IsSuccessStatusCode, importBody);

        using var importDoc = JsonDocument.Parse(importBody);
        var batchId = importDoc.RootElement.GetProperty("batchId").GetGuid();

        var reconUrl =
            $"api/hr-core/api/establishment/finance/reconciliation?fiscalYear={fiscalYear}&batchId={batchId:D}&orgUnitId={AcceptanceSeedIds.Br1OrgUnitId:D}";
        var reconResponse = await http.GetAsync(reconUrl);
        var reconBody = await reconResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("finance-reconciliation", "GET", $"/{reconUrl}", (int)reconResponse.StatusCode, JsonSerializer.Deserialize<object>(reconBody));
        Assert.True(reconResponse.IsSuccessStatusCode, reconBody);

        using var reconDoc = JsonDocument.Parse(reconBody);
        var rows = reconDoc.RootElement.GetProperty("rows");
        Assert.True(rows.GetArrayLength() > 0, "Expected reconciliation rows.");
        var hasVariance = rows.EnumerateArray().Any(r => r.GetProperty("countVariance").GetInt32() != 0);
        Assert.True(hasVariance, "Expected non-zero count variance vs funded positions.");

        recorder.WriteDbChecks(new
        {
            batchId,
            fiscalYear,
            orgUnitId = AcceptanceSeedIds.Br1OrgUnitId,
            rowCount = rows.GetArrayLength(),
            hasVariance,
            liveErp = false
        });
        recorder.WriteResult("Pass");
    }
}
