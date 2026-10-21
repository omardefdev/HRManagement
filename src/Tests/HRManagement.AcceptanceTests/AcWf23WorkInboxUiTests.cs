using System.Net;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-WF-23: unified work inbox UI.</summary>
public class AcWf23WorkInboxUiTests
{
    [SkippableFact]
    public async Task WorkInbox_index_lists_actionable_items_for_hr_core_user()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-WF-23a", correlationId);
        recorder.WriteMetadata("Work inbox for branch manager", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var resp = await http.GetAsync("ui/hr-core/WorkInbox");
        var body = await resp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("work-inbox-index", "GET", "/ui/hr-core/WorkInbox", (int)resp.StatusCode,
            body.Length > 800 ? body[..800] : body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("صندوق العمل", body, StringComparison.OrdinalIgnoreCase);

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Pending_approvals_api_returns_unified_count_shape()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-WF-23e", correlationId);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var resp = await http.GetAsync("ui/hr-core/api/stats/pending-approvals");
        var body = await resp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("pending-approvals", "GET", "/ui/hr-core/api/stats/pending-approvals",
            (int)resp.StatusCode, body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("\"count\"", body, StringComparison.Ordinal);
        Assert.Contains("\"workflowCount\"", body, StringComparison.Ordinal);
        Assert.Contains("\"legacyCount\"", body, StringComparison.Ordinal);

        recorder.WriteResult("Pass");
    }
}
