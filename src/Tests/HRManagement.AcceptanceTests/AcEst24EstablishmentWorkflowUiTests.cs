using System.Net;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-EST-24: establishment change workflow UI + work inbox.</summary>
public class AcEst24EstablishmentWorkflowUiTests
{
    [SkippableFact]
    public async Task Establishment_details_shows_workflow_panel_for_pending_change()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-24a", correlationId);
        recorder.WriteMetadata("Establishment workflow panel", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        // PLAN-41: the scenario submits its own change, as a user would; the seed has none and approvals consume them.
        var changeId = await EstablishmentChangeFlow.SubmitSampleChangeAsync(http, recorder, "AC-EST-24a");
        var listResp = await http.GetAsync("ui/hr-core/EstablishmentChanges?status=PendingApproval");
        var listBody = await listResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("list-pending-changes", "GET", "/ui/hr-core/EstablishmentChanges?status=PendingApproval",
            (int)listResp.StatusCode, listBody.Length > 500 ? listBody[..500] : listBody);

        AcceptanceGate.RequireData(recorder, listResp.StatusCode == HttpStatusCode.OK,
            "القائمة غير متاحة أو البيئة فارغة", "No establishment changes list access or empty stack.");

        var detailsHref = ExtractFirstDetailsHref(listBody, $"EstablishmentChanges/Details/{changeId:D}");
        Assert.True(detailsHref is not null, "The submitted change is missing from the PendingApproval list.");

        var detailsResp = await http.GetAsync(detailsHref!.TrimStart('/'));
        var detailsBody = await detailsResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("establishment-details", "GET", detailsHref, (int)detailsResp.StatusCode,
            detailsBody.Length > 800 ? detailsBody[..800] : detailsBody);

        Assert.Equal(HttpStatusCode.OK, detailsResp.StatusCode);
        Assert.True(
            detailsBody.Contains("مسار الاعتماد", StringComparison.OrdinalIgnoreCase)
            || detailsBody.Contains("WorkflowInstanceId", StringComparison.OrdinalIgnoreCase),
            "Details should show workflow panel or pending workflow state.");

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task WorkInbox_lists_the_deep_link_of_a_pending_establishment_change()
    {
        AcceptanceEnv.RequireEnabled();
        // PLAN-25: step 1 belongs to hr-core and never to the creator, so central-a2 sees what central-a submitted.
        var creator = AcceptanceHarness.RequirePersona("central-a");
        var persona = AcceptanceHarness.RequirePersona("central-a2");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-24e", correlationId);

        using var creatorHttp = await AcceptanceHarness.CreateAuthorizedClientAsync(creator, correlationId);
        var changeId = await EstablishmentChangeFlow.SubmitSampleChangeAsync(creatorHttp, recorder, "AC-EST-24e");

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var resp = await http.GetAsync("ui/hr-core/WorkInbox");
        var body = await resp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("work-inbox", "GET", "/ui/hr-core/WorkInbox", (int)resp.StatusCode,
            body.Length > 800 ? body[..800] : body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("صندوق العمل", body, StringComparison.OrdinalIgnoreCase);

        Assert.Contains($"EstablishmentChanges/Details/{changeId:D}", body, StringComparison.OrdinalIgnoreCase);
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Enforce_capstone_pending_api_shape_when_stack_available()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-24f", correlationId);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var resp = await http.GetAsync("ui/hr-core/api/stats/pending-approvals");
        var body = await resp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("pending-approvals", "GET", "/ui/hr-core/api/stats/pending-approvals",
            (int)resp.StatusCode, body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("\"workflowCount\"", body, StringComparison.Ordinal);

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
