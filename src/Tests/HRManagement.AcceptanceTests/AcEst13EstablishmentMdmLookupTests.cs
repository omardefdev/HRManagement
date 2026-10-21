using System.Text.Json;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-EST-13: MDM reference lookup for establishment forms.</summary>
public class AcEst13EstablishmentMdmLookupTests
{
    [SkippableFact]
    public async Task Reference_data_lookup_returns_national_grades()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-13", correlationId);
        recorder.WriteMetadata("AC-EST-13 MDM reference lookup", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var url = "api/hr-core/api/lookup/reference-data?kind=Grade&q=GR&page=1&pageSize=20";
        var response = await http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        recorder.CaptureHttp("lookup-grades", "GET", $"/{url}", (int)response.StatusCode, JsonSerializer.Deserialize<object>(body));

        Assert.True(response.IsSuccessStatusCode, body);
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "Expected at least one Grade entry from MDM seed.");

        recorder.WriteDbChecks(new { gradeLookupCount = items.GetArrayLength() });
        recorder.WriteResult("Pass");
    }
}
