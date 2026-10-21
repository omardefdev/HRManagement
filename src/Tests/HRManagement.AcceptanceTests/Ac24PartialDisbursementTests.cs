using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-24: partial disbursement creates batch, arrears, and payment orders with invariant.</summary>
public class Ac24PartialDisbursementTests
{
    [SkippableFact]
    public async Task Partial_disbursement_http_path_invariant_and_idempotency()
    {
        AcceptanceEnv.RequireEnabled();
        var processor = AcceptanceHarness.RequirePersona("payroll-processor");
        var approver = AcceptanceHarness.RequirePersona("payroll-approver");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(processor);
        var recorder = new EvidenceRecorder("AC-24", correlationId);
        recorder.WriteMetadata("AC-24 partial disbursement orchestrator (PAY-004)", processor.Id);

        using var httpProcessor = await AcceptanceHarness.CreateAuthorizedClientAsync(processor, correlationId);
        using var httpApprover = await AcceptanceHarness.CreateAuthorizedClientAsync(approver, correlationId);

        var periodStart = new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc);
        var create = await httpProcessor.PostAsync("api/payroll/runs", JsonContent(new
        {
            periodStart,
            periodEnd,
            runDate = DateTime.UtcNow
        }));
        var createBody = await create.Content.ReadAsStringAsync();
        Assert.True(create.IsSuccessStatusCode, createBody);

        Guid runId;
        using (var doc = JsonDocument.Parse(createBody))
            runId = doc.RootElement.GetProperty("id").GetGuid();

        await RequireSuccessAsync(
            await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/calculate", null),
            recorder, "calculate", $"/api/payroll/runs/{runId:D}/calculate");
        await RequireSuccessAsync(
            await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/validate", null),
            recorder, "validate", $"/api/payroll/runs/{runId:D}/validate");
        await RequireSuccessAsync(
            await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/submit-for-review", null),
            recorder, "submit-for-review", $"/api/payroll/runs/{runId:D}/submit-for-review");
        await RequireSuccessAsync(
            await httpApprover.PostAsync($"api/payroll/runs/{runId:D}/approve", null),
            recorder, "approve", $"/api/payroll/runs/{runId:D}/approve");

        var runResp = await httpProcessor.GetAsync($"api/payroll/runs/{runId:D}");
        var runJson = await runResp.Content.ReadAsStringAsync();
        using var runDoc = JsonDocument.Parse(runJson);
        var totalNet = runDoc.RootElement.GetProperty("totalNetPay").GetDecimal();
        recorder.WriteBefore(new { runId, totalNet });

        var fundingResp = await httpApprover.PostAsJsonAsync($"api/payroll/runs/{runId:D}/funding-decisions", new
        {
            requestedAmount = totalNet,
            approvedAmount = Math.Round(totalNet * 0.60m, 2),
            fundingRatio = 0.60m
        });
        var fundingBody = await fundingResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("funding-decision", "POST",
            $"/api/payroll/runs/{runId:D}/funding-decisions",
            (int)fundingResp.StatusCode, ParseEvidenceBody(fundingBody));

        Assert.True(fundingResp.IsSuccessStatusCode, fundingBody);

        Guid decisionId;
        using (var doc = JsonDocument.Parse(fundingBody))
            decisionId = doc.RootElement.GetProperty("id").GetGuid();

        var disburse = await httpProcessor.PostAsJsonAsync($"api/payroll/runs/{runId:D}/partial-disburse", new
        {
            fundingDecisionId = decisionId
        });
        var disburseBody = await disburse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("partial-disburse", "POST",
            $"/api/payroll/runs/{runId:D}/partial-disburse",
            (int)disburse.StatusCode, ParseEvidenceBody(disburseBody));

        Assert.True(disburse.IsSuccessStatusCode, disburseBody);

        var replay = await httpProcessor.PostAsJsonAsync($"api/payroll/runs/{runId:D}/partial-disburse", new
        {
            fundingDecisionId = decisionId
        });
        recorder.CaptureHttp("partial-disburse-replay", "POST",
            $"/api/payroll/runs/{runId:D}/partial-disburse",
            (int)replay.StatusCode, ParseEvidenceBody(await replay.Content.ReadAsStringAsync()));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, replay.StatusCode);

        using var disburseDoc = JsonDocument.Parse(disburseBody);
        var invariantOk = disburseDoc.RootElement.GetProperty("invariantOk").GetBoolean();
        recorder.WriteAfter(new { invariantOk, disburseBody = ParseEvidenceBody(disburseBody) });
        recorder.WriteDbChecks(new { invariantOk, idempotentReplay = replay.StatusCode == System.Net.HttpStatusCode.Conflict });
        Assert.True(invariantOk);

        recorder.WriteResult("Pass");
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task RequireSuccessAsync(
        HttpResponseMessage response,
        EvidenceRecorder recorder,
        string label,
        string path)
    {
        var body = await response.Content.ReadAsStringAsync();
        recorder.CaptureHttp(label, "POST", path, (int)response.StatusCode,
            string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<object>(body));
        Assert.True(response.IsSuccessStatusCode, $"{label}: {body}");
    }

    private static object? ParseEvidenceBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JsonSerializer.Deserialize<object>(body); }
        catch (JsonException) { return body; }
    }
}
