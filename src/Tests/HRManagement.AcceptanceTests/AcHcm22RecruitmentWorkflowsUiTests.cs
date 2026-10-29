using System.Net;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-HCM-22: Offer / committee / appeal workflow UI.</summary>
[RequiredAcceptance]
public class AcHcm22RecruitmentWorkflowsUiTests
{
    [SkippableFact]
    public async Task Offer_creator_details_page_blocks_self_approval()
    {
        AcceptanceEnv.RequireEnabled();
        var maker = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(maker);
        var recorder = new EvidenceRecorder("AC-HCM-22", correlationId);
        recorder.WriteMetadata("AC-HCM-22b offer creator SoD", maker.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(maker, correlationId);

        var listResp = await http.GetAsync("ui/hr-core/OfferLetters?status=PendingApproval");
        var listBody = await listResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("list-pending-offers", "GET", "/ui/hr-core/OfferLetters?status=PendingApproval",
            (int)listResp.StatusCode, listBody.Length > 500 ? listBody[..500] : listBody);

        AcceptanceGate.RequireData(recorder, listResp.StatusCode == HttpStatusCode.OK,
            "القائمة غير متاحة أو البيئة فارغة", "No offer list access or empty stack.");

        var detailsHref = ExtractFirstDetailsHref(listBody, "OfferLetters/Details/");
        AcceptanceGate.RequireData(recorder, detailsHref is not null,
            "لا يوجد سجل معلق مناسب للفحص", "No pending offer available for creator SoD check.");

        var detailsResp = await http.GetAsync(detailsHref!.TrimStart('/'));
        var detailsBody = await detailsResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("creator-offer-details", "GET", detailsHref, (int)detailsResp.StatusCode,
            detailsBody.Length > 800 ? detailsBody[..800] : detailsBody);

        Assert.Equal(HttpStatusCode.OK, detailsResp.StatusCode);
        Assert.True(
            detailsBody.Contains("لا يمكن للمنشئ اعتماد عرضه الخاص", StringComparison.OrdinalIgnoreCase)
            || !detailsBody.Contains(">اعتماد<", StringComparison.OrdinalIgnoreCase),
            "Creator should not see approve action on own pending offer.");

        recorder.WriteResult("Pass");
    }

    private static string? ExtractFirstDetailsHref(string html, string marker)
    {
        var idx = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = html.LastIndexOf("href=\"", idx, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += "href=\"".Length;
        var end = html.IndexOf('"', start);
        if (end < 0) return null;
        return html[start..end];
    }
}
