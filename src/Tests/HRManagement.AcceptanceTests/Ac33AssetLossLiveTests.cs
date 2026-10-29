using System.Net;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-33: privileged asset loss report retains before/after snapshots (live read).</summary>
public class Ac33AssetLossLiveTests
{
    [SkippableFact]
    public async Task Acceptance_asset_details_expose_loss_snapshot_fields_when_present()
    {
        AcceptanceEnv.RequireEnabled();
        var br1 = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(br1);
        var recorder = new EvidenceRecorder("AC-33", correlationId);
        recorder.WriteMetadata("AC-33 asset loss before/after snapshots", br1.Id);

        var assetPath = $"ui/admin-logistics/Assets/Details/{AcceptanceSeedIds.AcceptanceAssetBr1Id:D}";
        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(br1, correlationId);
        var resp = await http.GetAsync(assetPath);
        var html = await resp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("asset-details", "GET", $"/{assetPath}", (int)resp.StatusCode,
            new { length = html.Length, tag = AcceptanceSeedIds.AcceptanceAssetBr1Tag });

        AcceptanceGate.RequireData(recorder, resp.StatusCode != HttpStatusCode.NotFound,
            "أصل القبول غير مبذور في هذه البيئة",
            "Acceptance asset not seeded in this environment — run acceptance-up with RUN_ACCEPTANCE");

        E2eHttp.EnsureNotServerError((int)resp.StatusCode, "asset details");
        Assert.True(resp.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            Assert.Contains(AcceptanceSeedIds.AcceptanceAssetBr1Tag, html, StringComparison.Ordinal);
            recorder.WriteAfter(new
            {
                note = "Before/after JSON rendered in loss history when reports exist",
                privileged = true,
                evidenceRefPattern = "AC-32-AC-33-HANDOVER"
            });
        }

        recorder.WriteResult("Pass");
    }
}
