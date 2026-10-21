using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-25: fraud hold blocks pay; four-eyes release allows pay.</summary>
public class Ac25FraudHoldTests
{
    [SkippableFact]
    public async Task Fraud_hold_blocks_pay_until_four_eyes_release()
    {
        AcceptanceEnv.RequireEnabled();
        var processor = AcceptanceHarness.RequirePersona("payroll-processor");
        var approver = AcceptanceHarness.RequirePersona("payroll-approver");
        var poster = AcceptanceHarness.RequirePersona("a-payroll-poster");
        var auditor = AcceptanceHarness.RequirePersona("auditor");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(processor);
        var (approverToken, _) = await AcceptanceHarness.LoginAsync(approver);
        var (_, _) = await AcceptanceHarness.LoginAsync(poster);
        var (auditorToken, _) = await AcceptanceHarness.LoginAsync(auditor);
        var recorder = new EvidenceRecorder("AC-25", correlationId);
        recorder.WriteMetadata("AC-25 fraud hold four-eyes (FRD-001)", processor.Id);

        using var httpProcessor = await AcceptanceHarness.CreateAuthorizedClientAsync(processor, correlationId);
        using var httpApprover = await AcceptanceHarness.CreateAuthorizedClientAsync(approver, correlationId);
        using var httpPoster = await AcceptanceHarness.CreateAuthorizedClientAsync(poster, correlationId);

        var periodStart = new DateTime(2027, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var hireDay = periodStart;

        await RequireSuccessAsync(
            await httpProcessor.PutAsJsonAsync(
                $"api/payroll/employees/{AcceptanceSeedIds.EmpBr1Id:D}/pay-profile",
                new
                {
                    baseSalary = 475000m,
                    payGradeId = (Guid?)null,
                    effectiveFrom = hireDay,
                    effectiveTo = (DateTime?)null,
                    isActive = true,
                    changeReason = "AC-25 deterministic same-day hire-and-pay fixture"
                }),
            recorder,
            "prepare-fraud-fixture",
            $"/api/payroll/employees/{AcceptanceSeedIds.EmpBr1Id:D}/pay-profile",
            "PUT");

        var create = await httpProcessor.PostAsync("api/payroll/runs", JsonContent(new
        {
            periodStart,
            periodEnd,
            runDate = hireDay
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

        var approverSub = RequireSubject(approverToken);
        var auditorSub = RequireSubject(auditorToken);
        var (auditExit, auditOutput) = await AcceptanceMessagingHelper.RunScriptAsync(
            "acceptance-payroll-audit-fixture.ps1",
            ("RunId", runId.ToString("D")),
            ("AuditorId", auditorSub.ToString("D")),
            ("ClosedBy", approverSub.ToString("D")));
        recorder.WriteDbChecks(new { auditFixtureExitCode = auditExit, auditFixtureOutput = auditOutput });
        Assert.True(auditExit == 0, auditOutput);

        await RequireSuccessAsync(
            await httpPoster.PostAsync($"api/payroll/runs/{runId:D}/post", null),
            recorder, "post", $"/api/payroll/runs/{runId:D}/post");

        var scan = await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/fraud-scan", null);
        var scanBody = await scan.Content.ReadAsStringAsync();
        recorder.CaptureHttp("fraud-scan", "POST", $"/api/payroll/runs/{runId:D}/fraud-scan",
            (int)scan.StatusCode,
            string.IsNullOrWhiteSpace(scanBody) ? null : JsonSerializer.Deserialize<object>(scanBody));

        Assert.True(scan.IsSuccessStatusCode, scanBody);

        using var scanDoc = JsonDocument.Parse(scanBody);
        Assert.True(scanDoc.RootElement.TryGetProperty("holdIds", out var holdIdsEl));
        Assert.NotEqual(0, holdIdsEl.GetArrayLength());

        var holdIds = holdIdsEl.EnumerateArray().Select(x => x.GetGuid()).ToArray();
        var holdId = holdIds[0];

        var runResp = await httpProcessor.GetAsync($"api/payroll/runs/{runId:D}");
        var runJson = await runResp.Content.ReadAsStringAsync();
        using var runDoc = JsonDocument.Parse(runJson);
        var totalNet = runDoc.RootElement.GetProperty("totalNetPay").GetDecimal();

        var fundingResp = await httpApprover.PostAsJsonAsync($"api/payroll/runs/{runId:D}/funding-decisions", new
        {
            requestedAmount = totalNet,
            approvedAmount = totalNet,
            fundingRatio = 1.0m
        });
        Assert.True(fundingResp.IsSuccessStatusCode, await fundingResp.Content.ReadAsStringAsync());

        var fundingBody = await fundingResp.Content.ReadAsStringAsync();
        Guid decisionId;
        using (var doc = JsonDocument.Parse(fundingBody))
            decisionId = doc.RootElement.GetProperty("id").GetGuid();

        var disburse = await httpProcessor.PostAsJsonAsync($"api/payroll/runs/{runId:D}/partial-disburse", new
        {
            fundingDecisionId = decisionId
        });
        var disburseBody = await disburse.Content.ReadAsStringAsync();
        Assert.True(disburse.IsSuccessStatusCode, disburseBody);

        using var disburseDoc = JsonDocument.Parse(disburseBody);
        var batchId = disburseDoc.RootElement.GetProperty("batchId").GetGuid();

        var blockedPay = await httpPoster.PostAsync($"api/payroll/payment-batches/{batchId:D}/process", null);
        var blockedBody = await blockedPay.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-blocked-pay", "POST",
            $"/api/payroll/payment-batches/{batchId:D}/process",
            (int)blockedPay.StatusCode, JsonSerializer.Deserialize<object>(blockedBody));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, blockedPay.StatusCode);

        var soloRelease = await httpApprover.PostAsJsonAsync($"api/payroll/payment-holds/{holdId:D}/release", new
        {
            approverUserId = approverSub,
            releaseReason = "solo attempt"
        });
        recorder.CaptureHttp("solo-release-negative", "POST",
            $"/api/payroll/payment-holds/{holdId:D}/release",
            (int)soloRelease.StatusCode,
            JsonSerializer.Deserialize<object>(await soloRelease.Content.ReadAsStringAsync()));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, soloRelease.StatusCode);

        for (var i = 0; i < holdIds.Length; i++)
        {
            var release = await httpApprover.PostAsJsonAsync($"api/payroll/payment-holds/{holdIds[i]:D}/release", new
            {
                approverUserId = auditorSub,
                releaseReason = "cleared after independent review"
            });
            var releaseBody = await release.Content.ReadAsStringAsync();
            recorder.CaptureHttp($"http-release-{i + 1:D2}", "POST",
                $"/api/payroll/payment-holds/{holdIds[i]:D}/release",
                (int)release.StatusCode,
                string.IsNullOrWhiteSpace(releaseBody) ? null : JsonSerializer.Deserialize<object>(releaseBody));
            Assert.True(release.IsSuccessStatusCode, releaseBody);
        }

        var payAfter = await httpPoster.PostAsync($"api/payroll/payment-batches/{batchId:D}/process", null);
        var payAfterBody = await payAfter.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-pay-after-release", "POST",
            $"/api/payroll/payment-batches/{batchId:D}/process",
            (int)payAfter.StatusCode,
            string.IsNullOrWhiteSpace(payAfterBody) ? null : JsonSerializer.Deserialize<object>(payAfterBody));
        Assert.True(payAfter.IsSuccessStatusCode, payAfterBody);

        recorder.WriteResult("Pass");
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static Guid RequireSubject(string token)
    {
        var jwt = AcceptanceHarness.ParseJwt(token);
        Assert.True(Guid.TryParse(jwt.Subject, out var subject), "Persona token has no GUID subject.");
        return subject;
    }

    private static async Task RequireSuccessAsync(
        HttpResponseMessage response,
        EvidenceRecorder recorder,
        string label,
        string path,
        string method = "POST")
    {
        var body = await response.Content.ReadAsStringAsync();
        recorder.CaptureHttp(label, method, path, (int)response.StatusCode,
            string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<object>(body));
        Assert.True(response.IsSuccessStatusCode, $"{label}: {body}");
    }
}
