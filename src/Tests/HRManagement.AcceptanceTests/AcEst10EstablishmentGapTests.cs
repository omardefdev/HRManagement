using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-EST-10: establishment gap from FundedPosition with positive deficit.</summary>
public class AcEst10EstablishmentGapTests
{
    [SkippableFact]
    public async Task Gap_analysis_reflects_funded_counts_and_deficit()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-10", correlationId);
        recorder.WriteMetadata("AC-EST-10 establishment gap analysis", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var fiscalYear = AcceptanceSeedCatalog.ReferenceDate.Year;
        var url =
            $"api/hr-core/api/establishment/gap-analysis?orgUnitId={AcceptanceSeedIds.Br1OrgUnitId:D}&fiscalYear={fiscalYear}";

        var response = await http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        recorder.CaptureHttp("gap-analysis-br1", "GET", $"/{url}", (int)response.StatusCode, JsonSerializer.Deserialize<object>(body));
        Assert.True(response.IsSuccessStatusCode, body);

        using var doc = JsonDocument.Parse(body);
        var summary = doc.RootElement.GetProperty("summary");
        var actual = summary.GetProperty("actual");
        var occupied = actual.GetProperty("occupiedCount").GetInt32();
        var vacant = actual.GetProperty("vacantCount").GetInt32();
        var frozen = actual.GetProperty("frozenCount").GetInt32();
        var funded = actual.GetProperty("fundedCount").GetInt32();
        var deficit = summary.GetProperty("deficitCount").GetInt32();
        var source = doc.RootElement.GetProperty("source").GetString();

        Assert.Equal(funded, occupied + vacant + frozen);
        Assert.True(deficit > 0, "Expected positive deficit from seeded plan target (+2).");
        Assert.Equal("FundedPosition", source);

        recorder.WriteDbChecks(new
        {
            orgUnitId = AcceptanceSeedIds.Br1OrgUnitId,
            fiscalYear,
            funded,
            occupied,
            vacant,
            frozen,
            deficit,
            source,
            analyticsNotUsed = true
        });
        recorder.WriteResult("Pass");
    }
}
