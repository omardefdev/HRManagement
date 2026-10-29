using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-12: Payroll SoD — distinct maker/checker/poster; same-actor attempts denied.</summary>
public class Ac12PayrollSodTests
{
    [SkippableFact]
    public async Task Payroll_SoD_blocks_creator_approve_and_approver_post()
    {
        AcceptanceEnv.RequireEnabled();
        var processor = AcceptanceHarness.RequirePersona("payroll-processor");
        var approver = AcceptanceHarness.RequirePersona("payroll-approver");

        var (_, correlationId) = await AcceptanceHarness.LoginAsync(processor);
        var recorder = new EvidenceRecorder("AC-12", correlationId);
        recorder.WriteMetadata("AC-12 payroll SoD", processor.Id);

        using var httpProcessor = await AcceptanceHarness.CreateAuthorizedClientAsync(processor, correlationId);
        using var httpApprover = await AcceptanceHarness.CreateAuthorizedClientAsync(approver, correlationId);

        var periodStart = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var createPayload = new
        {
            periodStart,
            periodEnd,
            runDate = DateTime.UtcNow
        };

        var createResp = await httpProcessor.PostAsync("api/payroll/runs", JsonContent(createPayload));
        var createBody = await createResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("create-run", "POST", "/api/payroll/runs",
            (int)createResp.StatusCode, JsonSerializer.Deserialize<object>(createBody));
        Assert.True(createResp.IsSuccessStatusCode, createBody);

        Guid runId;
        using (var doc = JsonDocument.Parse(createBody))
        {
            runId = doc.RootElement.GetProperty("id").GetGuid();
        }

        var selfApproveResp = await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/approve", null);
        var selfApproveBody = await selfApproveResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("approve-self-negative", "POST", $"/api/payroll/runs/{runId:D}/approve",
            (int)selfApproveResp.StatusCode, ParseEvidenceBody(selfApproveBody));
        // The processor has no approval role by design. The request is rejected by
        // authorization before the command handler, so there is no JSON error body.
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, selfApproveResp.StatusCode);

        var submitResp = await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/submit-for-review", null);
        recorder.CaptureHttp("submit-for-review", "POST", $"/api/payroll/runs/{runId:D}/submit-for-review",
            (int)submitResp.StatusCode, await submitResp.Content.ReadAsStringAsync());

        AcceptanceGate.RequireData(recorder, submitResp.IsSuccessStatusCode,
            "تعذّر إرسال مسيّر الرواتب للمراجعة، فلا يمكن تنفيذ فحص فصل المهام لمنع المعتمد من الترحيل",
            "Payroll run could not be submitted for review, so the approver-post SoD check cannot run.");

        var approveResp = await httpApprover.PostAsync($"api/payroll/runs/{runId:D}/approve", null);
        var approveBody = await approveResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("approve-distinct-checker", "POST", $"/api/payroll/runs/{runId:D}/approve",
            (int)approveResp.StatusCode, JsonSerializer.Deserialize<object>(approveBody));

        AcceptanceGate.RequireData(recorder, approveResp.IsSuccessStatusCode,
            "تعذّر اعتماد مسيّر الرواتب، فلا يمكن تنفيذ فحص فصل المهام لمنع المعتمد من الترحيل",
            "Payroll run could not be approved, so the approver-post SoD check cannot run.");

        var approverPostResp = await httpApprover.PostAsync($"api/payroll/runs/{runId:D}/post", null);
        var approverPostBody = await approverPostResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("post-approver-negative", "POST", $"/api/payroll/runs/{runId:D}/post",
            (int)approverPostResp.StatusCode, JsonSerializer.Deserialize<object>(approverPostBody));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, approverPostResp.StatusCode);
        Assert.Contains("ترحيل", approverPostBody, StringComparison.Ordinal);

        recorder.WriteDbChecks(new
        {
            runId,
            creatorCannotApprove = true,
            approverCannotPostWhenApproved = true
        });
        recorder.WriteAfter(new { runId, sodEnforced = true });
        recorder.WriteResult("Pass");
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static object? ParseEvidenceBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JsonSerializer.Deserialize<object>(body); }
        catch (JsonException) { return body; }
    }
}
