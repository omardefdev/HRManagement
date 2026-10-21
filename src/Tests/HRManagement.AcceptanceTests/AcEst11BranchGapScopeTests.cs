using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-EST-11: branch manager scoped to BR1; sibling BR2 forbidden.</summary>
public class AcEst11BranchGapScopeTests
{
    [SkippableFact]
    public async Task Br1_manager_allowed_on_br1_forbidden_on_br2()
    {
        AcceptanceEnv.RequireEnabled();
        var br1 = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(br1);
        var recorder = new EvidenceRecorder("AC-EST-11", correlationId);
        recorder.WriteMetadata("AC-EST-11 branch gap scope", br1.Id);

        using var br1Http = await AcceptanceHarness.CreateAuthorizedClientAsync(br1, correlationId);
        var fiscalYear = AcceptanceSeedCatalog.ReferenceDate.Year;
        var br1Url =
            $"api/hr-core/api/establishment/gap-analysis?orgUnitId={AcceptanceSeedIds.Br1OrgUnitId:D}&fiscalYear={fiscalYear}";
        var br1Response = await br1Http.GetAsync(br1Url);
        var br1Body = await br1Response.Content.ReadAsStringAsync();
        recorder.CaptureHttp("gap-br1", "GET", $"/{br1Url}", (int)br1Response.StatusCode, JsonSerializer.Deserialize<object>(br1Body));
        Assert.True(br1Response.IsSuccessStatusCode, br1Body);

        var br2Url =
            $"api/hr-core/api/establishment/gap-analysis?orgUnitId={AcceptanceSeedIds.Br2OrgUnitId:D}&fiscalYear={fiscalYear}";
        var br2Response = await br1Http.GetAsync(br2Url);
        var br2Body = await br2Response.Content.ReadAsStringAsync();
        recorder.CaptureHttp("gap-br2-forbidden", "GET", $"/{br2Url}", (int)br2Response.StatusCode, br2Body);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, br2Response.StatusCode);

        recorder.WriteDbChecks(new
        {
            br1Scope = AcceptanceSeedIds.Br1OrgUnitId,
            br2Hidden = AcceptanceSeedIds.Br2OrgUnitId,
            br1Status = (int)br1Response.StatusCode,
            br2Status = (int)br2Response.StatusCode
        });
        recorder.WriteResult("Pass");
    }
}
