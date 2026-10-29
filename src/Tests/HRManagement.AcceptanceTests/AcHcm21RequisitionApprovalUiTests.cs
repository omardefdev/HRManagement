using System.Net;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-HCM-21: Requisition approval UI — workflow panel and SoD via MVC/API.</summary>
[RequiredAcceptance]
public class AcHcm21RequisitionApprovalUiTests
{
    [SkippableFact]
    public async Task Creator_details_page_does_not_expose_approve_action_for_own_requisition()
    {
        AcceptanceEnv.RequireEnabled();
        var maker = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(maker);
        var recorder = new EvidenceRecorder("AC-HCM-21", correlationId);
        recorder.WriteMetadata("AC-HCM-21b creator cannot self-approve", maker.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(maker, correlationId);

        var listResp = await http.GetAsync("ui/hr-core/RecruitmentRequisitions?status=PendingApproval");
        var listBody = await listResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("list-pending", "GET", "/ui/hr-core/RecruitmentRequisitions?status=PendingApproval",
            (int)listResp.StatusCode, listBody.Length > 500 ? listBody[..500] : listBody);

        AcceptanceGate.RequireData(recorder, listResp.StatusCode == HttpStatusCode.OK,
            "القائمة غير متاحة أو البيئة فارغة", "No pending requisitions list access or empty stack.");

        var detailsHref = ExtractFirstDetailsHref(listBody);
        AcceptanceGate.RequireData(recorder, detailsHref is not null,
            "لا يوجد سجل معلق مناسب للفحص", "No pending requisition available for creator SoD check.");

        var detailsResp = await http.GetAsync(detailsHref!.TrimStart('/'));
        var detailsBody = await detailsResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("creator-details", "GET", detailsHref, (int)detailsResp.StatusCode,
            detailsBody.Length > 800 ? detailsBody[..800] : detailsBody);

        Assert.Equal(HttpStatusCode.OK, detailsResp.StatusCode);
        Assert.DoesNotContain("name=\"Comments\"", detailsBody, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            detailsBody.Contains("لا يمكن للمنشئ اعتماد طلبه الخاص", StringComparison.OrdinalIgnoreCase)
            || !detailsBody.Contains(">اعتماد<", StringComparison.OrdinalIgnoreCase),
            "Creator should not see approve action on own pending requisition.");

        recorder.WriteResult("Pass");
    }

    private static string? ExtractFirstDetailsHref(string html)
    {
        const string marker = "RecruitmentRequisitions/Details/";
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
