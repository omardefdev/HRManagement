using System.Net;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-32: branch org-scope on logistics custody (live via gateway).</summary>
public class Ac32LogisticsScopeLiveTests
{
    [SkippableFact]
    public async Task Br2_manager_denied_br1_custody_asset_details()
    {
        AcceptanceEnv.RequireEnabled();
        var br2 = AcceptanceHarness.RequirePersona("br2-mgr");
        var br1 = AcceptanceHarness.RequirePersona("br1-mgr");
        var central = AcceptanceHarness.RequirePersona("a-logistics-maker");

        var (_, br2Correlation) = await AcceptanceHarness.LoginAsync(br2);
        var recorder = new EvidenceRecorder("AC-32", br2Correlation);
        recorder.WriteMetadata("AC-32 logistics custody org scope", br2.Id);

        var assetPath = $"ui/admin-logistics/Assets/Details/{AcceptanceSeedIds.AcceptanceAssetBr1Id:D}";

        using var br2Http = await AcceptanceHarness.CreateAuthorizedClientAsync(br2, br2Correlation);
        var deniedResp = await br2Http.GetAsync(assetPath);
        var deniedBody = await deniedResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("br2-details-denied", "GET", $"/{assetPath}", (int)deniedResp.StatusCode,
            new { length = deniedBody.Length });
        Assert.Equal(HttpStatusCode.Forbidden, deniedResp.StatusCode);

        var (_, br1Correlation) = await AcceptanceHarness.LoginAsync(br1);
        using var br1Http = await AcceptanceHarness.CreateAuthorizedClientAsync(br1, br1Correlation);
        var br1Resp = await br1Http.GetAsync(assetPath);
        recorder.CaptureHttp("br1-details-allowed", "GET", $"/{assetPath}", (int)br1Resp.StatusCode,
            new { status = br1Resp.StatusCode.ToString() });
        AcceptanceGate.RequireData(recorder, br1Resp.StatusCode != HttpStatusCode.NotFound,
            "أصل القبول الخاص بالفرع الأول غير مُهيّأ في بيانات البذر — شغّل scripts/acceptance-reset.ps1",
            "Acceptance asset not seeded (BR1 custody asset details returned 404) — run scripts/acceptance-reset.ps1.");
        Assert.Equal(HttpStatusCode.OK, br1Resp.StatusCode);

        var (_, centralCorrelation) = await AcceptanceHarness.LoginAsync(central);
        using var centralHttp = await AcceptanceHarness.CreateAuthorizedClientAsync(central, centralCorrelation);
        var centralResp = await centralHttp.GetAsync(assetPath);
        recorder.CaptureHttp("central-details-allowed", "GET", $"/{assetPath}", (int)centralResp.StatusCode,
            new { status = centralResp.StatusCode.ToString() });
        Assert.Equal(HttpStatusCode.OK, centralResp.StatusCode);

        recorder.WriteDbChecks(new
        {
            assetId = AcceptanceSeedIds.AcceptanceAssetBr1Id,
            holderEmployeeId = AcceptanceSeedIds.EmpBr1Id,
            br2Denied = (int)deniedResp.StatusCode
        });
        recorder.WriteResult("Pass");
    }
}
